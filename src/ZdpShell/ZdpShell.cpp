// Zero Data Peek - Dosya Gezgini sağ tık menüsü.
//
// Windows 11'in yeni bağlam menüsü (ve Windows 10'un klasik menüsü) paketli uygulamalardan
// IExplorerCommand uygulayan bir COM sınıfı ister. Bu DLL tek bir komut üretir: "Zero Data Peek ile aç".
// Komut yalnızca kullanıcının Ayarlar'da işaretlediği dosya türlerinde görünür; liste, uygulamanın
// LocalState klasörüne yazdığı shell-types.txt dosyasından okunur (her satırda bir uzantı, ör. ".parquet").
// Asıl işi yapmaz: seçili yolları ZeroDataPeek.exe'ye komut satırıyla iletir.

#include <windows.h>
#include <shobjidl_core.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <appmodel.h>
#include <new>
#include <string>
#include <vector>

namespace
{
    HINSTANCE g_module = nullptr;
    long g_objects = 0;

    // {5752260C-3CA3-4DC6-A53F-8E859ECC2D3A} - Package.appxmanifest ile aynı
    const CLSID CLSID_ZdpMenu = { 0x5752260c, 0x3ca3, 0x4dc6, { 0xa5, 0x3f, 0x8e, 0x85, 0x9e, 0xcc, 0x2d, 0x3a } };

    // Ayar dosyası yoksa (uygulama henüz hiç açılmadıysa) kullanılan liste; Services/ShellMenu.cs ile aynı tutulmalı.
    const wchar_t* const kDefaultExtensions[] =
    {
        L".csv", L".tsv", L".parquet", L".json", L".jsonl", L".xlsx", L".zdp",
    };

    bool IsTurkish()
    {
        return PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_TURKISH;
    }

    std::wstring ToLower(std::wstring text)
    {
        if (!text.empty()) CharLowerBuffW(text.data(), static_cast<DWORD>(text.size()));
        return text;
    }

    std::wstring Extension(const std::wstring& path)
    {
        size_t slash = path.find_last_of(L"\\/");
        size_t dot = path.find_last_of(L'.');
        if (dot == std::wstring::npos || (slash != std::wstring::npos && dot < slash)) return L"";
        return ToLower(path.substr(dot));
    }

    bool IsDirectory(const std::wstring& path)
    {
        DWORD attributes = GetFileAttributesW(path.c_str());
        return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
    }

    // %LOCALAPPDATA%\Packages\<paket ailesi>\LocalState\shell-types.txt
    std::wstring SettingsPath()
    {
        UINT32 length = 0;
        if (GetCurrentPackageFamilyName(&length, nullptr) != ERROR_INSUFFICIENT_BUFFER || length == 0) return L"";
        std::wstring family(length, L'\0');
        if (GetCurrentPackageFamilyName(&length, family.data()) != ERROR_SUCCESS) return L"";
        family.resize(wcslen(family.c_str()));

        PWSTR local = nullptr;
        if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &local)) || local == nullptr) return L"";
        std::wstring path = local;
        CoTaskMemFree(local);
        return path + L"\\Packages\\" + family + L"\\LocalState\\shell-types.txt";
    }

    // Etkin uzantılar (küçük harf, noktalı). Dosya yoksa varsayılan liste; dosya boşsa hiçbiri.
    std::vector<std::wstring> EnabledExtensions()
    {
        std::vector<std::wstring> result;
        std::wstring path = SettingsPath();
        HANDLE handle = path.empty()
            ? INVALID_HANDLE_VALUE
            : CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (handle == INVALID_HANDLE_VALUE)
        {
            for (const wchar_t* extension : kDefaultExtensions) result.emplace_back(extension);
            return result;
        }

        char buffer[4096] = {};
        DWORD read = 0;
        BOOL ok = ReadFile(handle, buffer, sizeof(buffer) - 1, &read, nullptr);
        CloseHandle(handle);
        if (!ok) return result;

        // Uzantılar ASCII'dir: bayt bayt genişletmek yeterli.
        std::wstring current;
        for (DWORD i = 0; i <= read; i++)
        {
            char c = i < read ? buffer[i] : '\n';
            if (c == '\n' || c == '\r')
            {
                if (current.size() > 1 && current[0] == L'.') result.push_back(ToLower(current));
                current.clear();
            }
            else if (c != ' ' && c != '\t' && static_cast<unsigned char>(c) < 128)
            {
                current.push_back(static_cast<wchar_t>(c));
            }
        }
        return result;
    }

    struct Selection
    {
        std::vector<std::wstring> paths;
        bool supported = false;
    };

    Selection ReadSelection(IShellItemArray* items)
    {
        Selection selection;
        if (items == nullptr) return selection;
        DWORD count = 0;
        if (FAILED(items->GetCount(&count)) || count == 0) return selection;
        if (count > 100) count = 100; // komut satırı sınırı: en çok 100 dosya açılır
        selection.paths.reserve(count);
        for (DWORD i = 0; i < count; i++)
        {
            IShellItem* item = nullptr;
            if (FAILED(items->GetItemAt(i, &item)) || item == nullptr) continue;
            PWSTR path = nullptr;
            if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)) && path != nullptr)
            {
                selection.paths.emplace_back(path);
                CoTaskMemFree(path);
            }
            item->Release();
        }
        if (selection.paths.empty()) return selection;

        // Seçimdeki her dosyanın türü etkin olmalı (menü hızlı açılsın diye ilk 32 öğeye bakılır).
        std::vector<std::wstring> enabled = EnabledExtensions();
        size_t probe = selection.paths.size() < 32 ? selection.paths.size() : 32;
        selection.supported = !enabled.empty();
        for (size_t i = 0; i < probe && selection.supported; i++)
        {
            std::wstring extension = Extension(selection.paths[i]);
            bool found = false;
            for (const auto& candidate : enabled)
            {
                if (candidate == extension)
                {
                    found = true;
                    break;
                }
            }
            if (!found || IsDirectory(selection.paths[i])) selection.supported = false;
        }
        return selection;
    }

    std::wstring ModuleDirectory()
    {
        std::wstring buffer(MAX_PATH, L'\0');
        for (;;)
        {
            DWORD length = GetModuleFileNameW(g_module, buffer.data(), static_cast<DWORD>(buffer.size()));
            if (length == 0) return L"";
            if (length < buffer.size())
            {
                buffer.resize(length);
                break;
            }
            buffer.resize(buffer.size() * 2);
        }
        size_t slash = buffer.find_last_of(L'\\');
        return slash == std::wstring::npos ? L"" : buffer.substr(0, slash + 1);
    }

    std::wstring ExePath()
    {
        return ModuleDirectory() + L"ZeroDataPeek.exe";
    }

    // CommandLineToArgvW kurallarına göre tırnaklar: tırnaktan önceki ters eğik çizgiler ikilenir.
    std::wstring Quote(const std::wstring& argument)
    {
        std::wstring result = L"\"";
        size_t backslashes = 0;
        for (wchar_t c : argument)
        {
            if (c == L'\\')
            {
                backslashes++;
            }
            else if (c == L'"')
            {
                result.append(backslashes + 1, L'\\');
                backslashes = 0;
            }
            else
            {
                backslashes = 0;
            }
            result.push_back(c);
        }
        result.append(backslashes, L'\\');
        result.push_back(L'"');
        return result;
    }

    HRESULT Launch(const Selection& selection)
    {
        if (selection.paths.empty()) return S_OK;

        std::wstring exe = ExePath();
        std::wstring commandLine = Quote(exe);
        for (const auto& path : selection.paths)
        {
            if (commandLine.size() + path.size() * 2 + 3 > 30000) break;
            commandLine += L" " + Quote(path);
        }

        // Çalışma klasörü: ilk öğenin bulunduğu klasör.
        std::wstring directory = selection.paths[0];
        size_t slash = directory.find_last_of(L'\\');
        directory = slash == std::wstring::npos ? L"" : directory.substr(0, slash + 1);

        STARTUPINFOW startup = {};
        startup.cb = sizeof(startup);
        PROCESS_INFORMATION process = {};
        std::vector<wchar_t> buffer(commandLine.begin(), commandLine.end());
        buffer.push_back(L'\0');
        if (!CreateProcessW(exe.c_str(), buffer.data(), nullptr, nullptr, FALSE, 0, nullptr,
                            directory.empty() ? nullptr : directory.c_str(), &startup, &process))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return S_OK;
    }

    // ------------------------------------------------------------------ "Zero Data Peek ile aç"

    class OpenCommand final : public IExplorerCommand
    {
    public:
        OpenCommand()
        {
            InterlockedIncrement(&g_objects);
        }

        IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
        {
            if (ppv == nullptr) return E_POINTER;
            if (riid == IID_IUnknown || riid == IID_IExplorerCommand)
            {
                *ppv = static_cast<IExplorerCommand*>(this);
                AddRef();
                return S_OK;
            }
            *ppv = nullptr;
            return E_NOINTERFACE;
        }

        IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_refs); }

        IFACEMETHODIMP_(ULONG) Release() override
        {
            long refs = InterlockedDecrement(&_refs);
            if (refs == 0) delete this;
            return refs;
        }

        IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* name) override
        {
            if (name == nullptr) return E_POINTER;
            return SHStrDupW(IsTurkish() ? L"Zero Data Peek ile a\x00e7" : L"Open with Zero Data Peek", name);
        }

        IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
        {
            if (icon == nullptr) return E_POINTER;
            std::wstring path = ExePath() + L",0";
            return SHStrDupW(path.c_str(), icon);
        }

        IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) override
        {
            if (tip != nullptr) *tip = nullptr;
            return E_NOTIMPL;
        }

        IFACEMETHODIMP GetCanonicalName(GUID* name) override
        {
            if (name != nullptr) *name = CLSID_ZdpMenu;
            return S_OK;
        }

        IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
        {
            if (state == nullptr) return E_POINTER;
            *state = ReadSelection(items).supported ? ECS_ENABLED : ECS_HIDDEN;
            return S_OK;
        }

        IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
        {
            Selection selection = ReadSelection(items);
            return selection.supported ? Launch(selection) : S_OK;
        }

        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
        {
            if (flags == nullptr) return E_POINTER;
            *flags = ECF_DEFAULT;
            return S_OK;
        }

        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override
        {
            if (commands != nullptr) *commands = nullptr;
            return E_NOTIMPL;
        }

    private:
        ~OpenCommand() { InterlockedDecrement(&g_objects); }

        long _refs = 1;
    };

    class ClassFactory final : public IClassFactory
    {
    public:
        ClassFactory() { InterlockedIncrement(&g_objects); }

        IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
        {
            if (ppv == nullptr) return E_POINTER;
            if (riid == IID_IUnknown || riid == IID_IClassFactory)
            {
                *ppv = static_cast<IClassFactory*>(this);
                AddRef();
                return S_OK;
            }
            *ppv = nullptr;
            return E_NOINTERFACE;
        }

        IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_refs); }

        IFACEMETHODIMP_(ULONG) Release() override
        {
            long refs = InterlockedDecrement(&_refs);
            if (refs == 0) delete this;
            return refs;
        }

        IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override
        {
            if (ppv == nullptr) return E_POINTER;
            *ppv = nullptr;
            if (outer != nullptr) return CLASS_E_NOAGGREGATION;
            auto* command = new (std::nothrow) OpenCommand();
            if (command == nullptr) return E_OUTOFMEMORY;
            HRESULT result = command->QueryInterface(riid, ppv);
            command->Release();
            return result;
        }

        IFACEMETHODIMP LockServer(BOOL lock) override
        {
            if (lock) InterlockedIncrement(&g_objects);
            else InterlockedDecrement(&g_objects);
            return S_OK;
        }

    private:
        ~ClassFactory() { InterlockedDecrement(&g_objects); }

        long _refs = 1;
    };
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, LPVOID* ppv)
{
    if (ppv == nullptr) return E_POINTER;
    *ppv = nullptr;
    if (clsid != CLSID_ZdpMenu) return CLASS_E_CLASSNOTAVAILABLE;
    auto* factory = new (std::nothrow) ClassFactory();
    if (factory == nullptr) return E_OUTOFMEMORY;
    HRESULT result = factory->QueryInterface(riid, ppv);
    factory->Release();
    return result;
}

STDAPI DllCanUnloadNow()
{
    return g_objects == 0 ? S_OK : S_FALSE;
}
