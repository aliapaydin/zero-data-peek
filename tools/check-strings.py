"""Kaynaktaki Türkçe arayüz metinlerini çıkarır ve Strings/en.json ile karşılaştırır."""
import json, re, sys, pathlib
root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "src/ZeroDataPeek")
keys = set()
cs_str = r'"((?:[^"\\]|\\.)*)"'
for f in root.rglob("*.cs"):
    if "obj" in f.parts or "bin" in f.parts: continue
    s = "\n".join(l for l in f.read_text(encoding="utf-8").splitlines() if not l.lstrip().startswith("//"))
    for m in re.finditer(r'Loc\.[TF]\(\s*' + cs_str, s): keys.add(m.group(1))
    for m in re.finditer(r'\bAdd\("\w+",\s*' + cs_str, s): keys.add(m.group(1))
    for m in re.finditer(r'new EncodingChoice\("[^"]+",\s*' + cs_str, s): keys.add(m.group(1))
    for m in re.finditer(r'new\("[\w-]+",\s*' + cs_str + r',\s*\(\)', s): keys.add(m.group(1))
keys = {k.replace('\\"', '"').replace('\\n', '\n') for k in keys}
attrs = r'(?:Text|Title|Content|PlaceholderText|Header|Label|ToolTipService\.ToolTip|AutomationProperties\.Name)'
for f in root.rglob("*.xaml"):
    if "obj" in f.parts or "bin" in f.parts: continue
    s = f.read_text(encoding="utf-8")
    for m in re.finditer(r'\s' + attrs + r'="([^"{][^"]*)"', s):
        keys.add(m.group(1).replace("&quot;", '"').replace("&amp;", "&"))
en = json.loads((root / "Strings" / "en.json").read_text(encoding="utf-8"))
same_ok = {"Zero Data Peek", "UTF-8", "UTF-8 BOM", "UTF-16 LE", "UTF-16 BE", "Markdown", "CSV", "JSON", "XML", "SQL", "Python"}
missing = sorted(k for k in keys if k not in en and k not in same_ok)
unused = sorted(k for k in en if k not in keys)
print(f"{len(keys)} metin, {len(en)} çeviri")
for k in missing: print("EKSİK:", repr(k))
for k in unused: print("KULLANILMIYOR:", repr(k))
sys.exit(1 if missing else 0)
