// Zero Data Peek belge modu: DOCX okuma ve yazma.
//
// Okuma: word/document.xml → HTML (paragraf, başlık, liste, tablo, resim, bağlantı ve karakter biçimleri).
// Yazma: düzenleyicideki HTML → asgari ama geçerli bir DOCX paketi.
// Desteklenmeyen öğeler (üstbilgi, dipnot, yorum, metin kutusu…) okunurken atlanır ve "losses" listesinde bildirilir;
// uygulama, kullanıcı böyle bir belgenin üzerine kaydetmeden önce uyarır.
(function () {
  'use strict';

  var W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main';
  var R = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships';
  var A = 'http://schemas.openxmlformats.org/drawingml/2006/main';

  var HIGHLIGHTS = {
    yellow: '#FFFF00', green: '#00FF00', cyan: '#00FFFF', magenta: '#FF00FF', blue: '#0000FF', red: '#FF0000',
    darkBlue: '#000080', darkCyan: '#008080', darkGreen: '#008000', darkMagenta: '#800080', darkRed: '#800000',
    darkYellow: '#808000', darkGray: '#808080', lightGray: '#C0C0C0', black: '#000000', white: '#FFFFFF'
  };
  var MIME = { png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', gif: 'image/gif', bmp: 'image/bmp', webp: 'image/webp', svg: 'image/svg+xml' };

  // ------------------------------------------------------------------ yardımcılar

  function kids(node, name) {
    var out = [];
    if (!node) return out;
    for (var c = node.firstChild; c; c = c.nextSibling) {
      if (c.nodeType === 1 && c.namespaceURI === W && (!name || c.localName === name)) out.push(c);
    }
    return out;
  }
  function kid(node, name) { return kids(node, name)[0] || null; }
  function wattr(node, name) { return node ? (node.getAttributeNS(W, name) || node.getAttribute('w:' + name)) : null; }
  function onOff(node) {
    if (!node) return undefined;
    var v = wattr(node, 'val');
    return !(v === '0' || v === 'false' || v === 'off' || v === 'none');
  }
  function esc(text) {
    return String(text).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }
  function cssFont(name) { return "'" + String(name).replace(/['\\]/g, '') + "'"; }

  // ================================================================== OKUMA

  function readRunProps(rPr, theme) {
    var p = {};
    if (!rPr) return p;
    var v;
    if ((v = onOff(kid(rPr, 'b'))) !== undefined) p.bold = v;
    if ((v = onOff(kid(rPr, 'i'))) !== undefined) p.italic = v;
    if ((v = onOff(kid(rPr, 'strike'))) !== undefined) p.strike = v;
    var u = kid(rPr, 'u');
    if (u) p.underline = wattr(u, 'val') !== 'none';
    var color = kid(rPr, 'color');
    if (color) {
      var cv = wattr(color, 'val');
      if (cv && cv !== 'auto' && /^[0-9a-fA-F]{6}$/.test(cv)) p.color = '#' + cv.toUpperCase();
    }
    var hl = kid(rPr, 'highlight');
    if (hl) { var hv = wattr(hl, 'val'); p.highlight = hv === 'none' ? null : (HIGHLIGHTS[hv] || null); }
    var shd = kid(rPr, 'shd');
    if (shd && p.highlight === undefined) {
      var fill = wattr(shd, 'fill');
      if (fill && fill !== 'auto' && /^[0-9a-fA-F]{6}$/.test(fill)) p.highlight = '#' + fill.toUpperCase();
    }
    var sz = kid(rPr, 'sz');
    if (sz) { var half = parseFloat(wattr(sz, 'val')); if (half > 0) p.size = half / 2; }
    var fonts = kid(rPr, 'rFonts');
    if (fonts) {
      var ascii = wattr(fonts, 'ascii') || wattr(fonts, 'hAnsi');
      var themed = wattr(fonts, 'asciiTheme') || wattr(fonts, 'hAnsiTheme');
      if (ascii) p.font = ascii;
      else if (themed && theme) p.font = /^major/.test(themed) ? theme.major : theme.minor;
    }
    var va = kid(rPr, 'vertAlign');
    if (va) { var vv = wattr(va, 'val'); p.vertical = vv === 'superscript' ? 'super' : vv === 'subscript' ? 'sub' : null; }
    return p;
  }

  function merge(base, over) {
    var out = {}, k;
    for (k in base) out[k] = base[k];
    for (k in over) if (over[k] !== undefined) out[k] = over[k];
    return out;
  }

  function readStyles(doc, theme) {
    var styles = { byId: {}, defaults: {}, defaultParagraph: null };
    if (!doc) return styles;
    var root = doc.documentElement;
    var dd = kid(root, 'docDefaults');
    if (dd) {
      var rd = kid(dd, 'rPrDefault');
      styles.defaults = readRunProps(rd ? kid(rd, 'rPr') : null, theme);
    }
    kids(root, 'style').forEach(function (s) {
      var id = wattr(s, 'styleId');
      if (!id) return;
      var nameNode = kid(s, 'name'), based = kid(s, 'basedOn'), pPr = kid(s, 'pPr');
      var entry = {
        id: id,
        type: wattr(s, 'type'),
        name: (nameNode ? wattr(nameNode, 'val') : id) || id,
        basedOn: based ? wattr(based, 'val') : null,
        run: readRunProps(kid(s, 'rPr'), theme),
        align: null, outline: null, num: null
      };
      if (pPr) {
        var jc = kid(pPr, 'jc'), ol = kid(pPr, 'outlineLvl'), np = kid(pPr, 'numPr');
        if (jc) entry.align = wattr(jc, 'val');
        if (ol) entry.outline = parseInt(wattr(ol, 'val'), 10);
        if (np) {
          var sn = kid(np, 'numId'), sl = kid(np, 'ilvl');
          entry.num = { numId: sn ? wattr(sn, 'val') : null, ilvl: sl ? wattr(sl, 'val') : null };
        }
      }
      styles.byId[id] = entry;
      if (entry.type === 'paragraph' && wattr(s, 'default') === '1') styles.defaultParagraph = id;
    });
    return styles;
  }

  // Stil zinciri: en temelden en özele.
  function chain(styles, id) {
    var list = [], seen = {};
    while (id && styles.byId[id] && !seen[id]) { seen[id] = true; list.unshift(styles.byId[id]); id = styles.byId[id].basedOn; }
    return list;
  }

  function headingLevel(styles, id) {
    var list = chain(styles, id);
    for (var i = list.length - 1; i >= 0; i--) {
      var m = /^heading\s*(\d)$/i.exec(list[i].name) || /^ba[sş]l[ıi]k\s*(\d)$/i.exec(list[i].name);
      if (m) return Math.min(6, Math.max(1, parseInt(m[1], 10)));
      if (/^title$/i.test(list[i].name)) return 1;
    }
    return 0;
  }

  function readNumbering(doc) {
    var result = {};
    if (!doc) return result;
    var root = doc.documentElement, abstracts = {};
    kids(root, 'abstractNum').forEach(function (a) {
      var levels = {};
      kids(a, 'lvl').forEach(function (l) {
        var fmt = kid(l, 'numFmt');
        levels[wattr(l, 'ilvl') || '0'] = fmt ? wattr(fmt, 'val') : 'decimal';
      });
      abstracts[wattr(a, 'abstractNumId')] = levels;
    });
    kids(root, 'num').forEach(function (n) {
      var ref = kid(n, 'abstractNumId');
      result[wattr(n, 'numId')] = abstracts[ref ? wattr(ref, 'val') : ''] || {};
    });
    return result;
  }

  function spanStyle(props, base) {
    var css = [];
    if (props.font && props.font !== base.font) css.push('font-family:' + cssFont(props.font));
    if (props.size && props.size !== base.size) css.push('font-size:' + props.size + 'pt');
    if (props.color && props.color !== base.color) css.push('color:' + props.color);
    if (props.highlight) css.push('background-color:' + props.highlight);
    return css.join(';');
  }

  function wrapRun(html, props, base, heading) {
    if (!html) return '';
    var css = spanStyle(props, base);
    if (css) html = '<span style="' + css + '">' + html + '</span>';
    if (props.bold && !heading) html = '<b>' + html + '</b>';
    if (props.italic) html = '<i>' + html + '</i>';
    if (props.underline) html = '<u>' + html + '</u>';
    if (props.strike) html = '<s>' + html + '</s>';
    if (props.vertical === 'super') html = '<sup>' + html + '</sup>';
    if (props.vertical === 'sub') html = '<sub>' + html + '</sub>';
    return html;
  }

  function Reader(ctx) { this.ctx = ctx; }

  Reader.prototype.image = function (drawing) {
    var blips = drawing.getElementsByTagNameNS(A, 'blip');
    if (!blips.length) return '';
    var id = blips[0].getAttributeNS(R, 'embed');
    var media = this.ctx.media[id];
    if (!media) return '';
    var style = '';
    var extents = drawing.getElementsByTagNameNS('http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing', 'extent');
    if (extents.length) {
      var cx = parseInt(extents[0].getAttribute('cx'), 10), cy = parseInt(extents[0].getAttribute('cy'), 10);
      if (cx > 0 && cy > 0) style = ' style="width:' + Math.round(cx / 9525) + 'px;height:' + Math.round(cy / 9525) + 'px"';
    }
    return '<img src="' + media + '"' + style + '>';
  };

  Reader.prototype.run = function (r, paraProps, base, heading) {
    var ctx = this.ctx, rPr = kid(r, 'rPr'), props = paraProps;
    if (rPr) {
      var rs = kid(rPr, 'rStyle');
      if (rs) chain(ctx.styles, wattr(rs, 'val')).forEach(function (s) { props = merge(props, s.run); });
      props = merge(props, readRunProps(rPr, ctx.theme));
    }
    var html = '';
    for (var c = r.firstChild; c; c = c.nextSibling) {
      if (c.nodeType !== 1) continue;
      var name = c.localName;
      if (c.namespaceURI === W) {
        if (name === 't') html += esc(c.textContent);
        else if (name === 'tab') html += '\t';
        else if (name === 'br' || name === 'cr') html += '<br>';
        else if (name === 'noBreakHyphen') html += '\u2011';
        else if (name === 'sym') { var ch = wattr(c, 'char'); if (ch) html += esc(String.fromCharCode(parseInt(ch, 16) & 0xFFFF)); }
        else if (name === 'drawing') html += this.image(c);
        else if (name === 'footnoteReference' || name === 'endnoteReference') ctx.losses.footnotes = true;
        else if (name === 'commentReference') ctx.losses.comments = true;
        else if (name === 'pict' || name === 'object') ctx.losses.objects = true;
      } else if (name === 'AlternateContent') {
        ctx.losses.textboxes = true;
      }
    }
    return wrapRun(html, props, base, heading);
  };

  Reader.prototype.inline = function (node, paraProps, base, heading) {
    var html = '';
    for (var c = node.firstChild; c; c = c.nextSibling) {
      if (c.nodeType !== 1 || c.namespaceURI !== W) continue;
      switch (c.localName) {
        case 'r': html += this.run(c, paraProps, base, heading); break;
        case 'hyperlink': {
          var id = c.getAttributeNS(R, 'id'), anchor = wattr(c, 'anchor');
          var href = id && this.ctx.links[id] ? this.ctx.links[id] : (anchor ? '#' + anchor : '');
          var inner = this.inline(c, paraProps, base, heading);
          html += href ? '<a href="' + esc(href) + '">' + inner + '</a>' : inner;
          break;
        }
        case 'ins': case 'smartTag': case 'fldSimple': html += this.inline(c, paraProps, base, heading); break;
        case 'sdt': { var content = kid(c, 'sdtContent'); if (content) html += this.inline(content, paraProps, base, heading); break; }
        default: break; // del, bookmarkStart, proofErr…
      }
    }
    return html;
  };

  // Paragrafı çözer: { html, list: {ordered, level} | null }
  Reader.prototype.paragraph = function (p) {
    var ctx = this.ctx, pPr = kid(p, 'pPr');
    var styleId = ctx.styles.defaultParagraph, align = null, list = null, indent = 0;
    if (pPr) {
      var ps = kid(pPr, 'pStyle');
      if (ps) styleId = wattr(ps, 'val');
    }
    var props = ctx.styles.defaults, styleNum = { numId: null, ilvl: null };
    chain(ctx.styles, styleId).forEach(function (s) {
      props = merge(props, s.run);
      if (s.align) align = s.align;
      if (s.num) { if (s.num.numId !== null) styleNum.numId = s.num.numId; if (s.num.ilvl !== null) styleNum.ilvl = s.num.ilvl; }
    });
    var level = headingLevel(ctx.styles, styleId);
    // Başlıklar görünümünü sayfanın başlık stilinden alır; yalnızca doğrudan verilen biçimler taşınır.
    if (level > 0) props = { font: ctx.base.font, size: ctx.base.size, color: ctx.base.color };
    var nid = styleNum.numId, lv = styleNum.ilvl;
    if (pPr) {
      var jc = kid(pPr, 'jc');
      if (jc) align = wattr(jc, 'val');
      var numPr = kid(pPr, 'numPr');
      if (numPr) {
        var numId = kid(numPr, 'numId'), ilvl = kid(numPr, 'ilvl');
        if (numId) nid = wattr(numId, 'val');
        if (ilvl) lv = wattr(ilvl, 'val');
      }
    }
    if (nid && nid !== '0' && level === 0) {
      var depthLevel = parseInt(lv, 10) || 0;
      var fmt = (ctx.numbering[nid] || {})[String(depthLevel)] || 'bullet';
      list = { ordered: fmt !== 'bullet' && fmt !== 'none', level: Math.min(depthLevel, 8), key: nid };
    }
    if (pPr) {
      var ind = kid(pPr, 'ind');
      if (ind && !list) { var left = parseInt(wattr(ind, 'left') || wattr(ind, 'start') || '0', 10); if (left > 0) indent = Math.round(left / 15); }
      var pRun = kid(pPr, 'rPr'); // paragraf işaretinin biçimi boş paragraflarda boyutu belirler
      if (pRun && !kids(p, 'r').length) props = merge(props, readRunProps(pRun, ctx.theme));
    }

    var inner = this.inline(p, props, ctx.base, level > 0);
    var css = [];
    var map = { center: 'center', right: 'right', end: 'right', both: 'justify', distribute: 'justify' };
    if (align && map[align]) css.push('text-align:' + map[align]);
    if (indent) css.push('margin-left:' + indent + 'px');
    var attr = css.length ? ' style="' + css.join(';') + '"' : '';
    if (!inner) inner = '<br>';
    if (list) return { html: inner, list: list, attr: attr };
    var tag = level > 0 ? 'h' + level : 'p';
    return { html: '<' + tag + attr + '>' + inner + '</' + tag + '>', list: null };
  };

  Reader.prototype.table = function (tbl) {
    var self = this, html = '<table>';
    kids(tbl, 'tr').forEach(function (tr) {
      html += '<tr>';
      kids(tr, 'tc').forEach(function (tc) {
        var tcPr = kid(tc, 'tcPr'), span = 1, skip = false;
        if (tcPr) {
          var gs = kid(tcPr, 'gridSpan');
          if (gs) span = parseInt(wattr(gs, 'val'), 10) || 1;
          var vm = kid(tcPr, 'vMerge');
          if (vm && wattr(vm, 'val') !== 'restart') skip = true; // dikey birleştirmenin devamı: boş hücre
        }
        html += '<td' + (span > 1 ? ' colspan="' + span + '"' : '') + '>' + (skip ? '<br>' : self.blocks(tc)) + '</td>';
      });
      html += '</tr>';
    });
    return html + '</table>';
  };

  // Ardışık liste paragraflarını iç içe ul/ol yapısına çevirir.
  function closeLists(stack, upto) {
    var html = '';
    while (stack.length > upto) { var top = stack.pop(); html += '</li></' + (top.ordered ? 'ol' : 'ul') + '>'; }
    return html;
  }

  Reader.prototype.blocks = function (container) {
    var html = '', stack = [];
    for (var c = container.firstChild; c; c = c.nextSibling) {
      if (c.nodeType !== 1 || c.namespaceURI !== W) continue;
      if (c.localName === 'p') {
        var para = this.paragraph(c);
        if (para.list) {
          var depth = para.list.level + 1;
          if (stack.length > depth) html += closeLists(stack, depth);
          if (stack.length === depth && stack[depth - 1].ordered !== para.list.ordered) html += closeLists(stack, depth - 1);
          if (stack.length === depth) html += '</li>';
          while (stack.length < depth) { html += '<' + (para.list.ordered ? 'ol' : 'ul') + '>'; stack.push({ ordered: para.list.ordered }); if (stack.length < depth) html += '<li>'; }
          html += '<li' + para.attr + '>' + para.html;
        } else {
          html += closeLists(stack, 0) + para.html;
        }
      } else if (c.localName === 'tbl') {
        html += closeLists(stack, 0) + this.table(c);
      } else if (c.localName === 'sdt') {
        var content = kid(c, 'sdtContent');
        if (content) html += closeLists(stack, 0) + this.blocks(content);
      }
    }
    return html + closeLists(stack, 0);
  };

  function parseXml(text) { return text ? new DOMParser().parseFromString(text, 'application/xml') : null; }

  function readDocx(base64) {
    var zip;
    return JSZip.loadAsync(base64, { base64: true }).then(function (z) {
      zip = z;
      var names = ['word/document.xml', 'word/styles.xml', 'word/numbering.xml', 'word/_rels/document.xml.rels', 'word/theme/theme1.xml'];
      return Promise.all(names.map(function (n) { return zip.file(n) ? zip.file(n).async('string') : Promise.resolve(null); }));
    }).then(function (parts) {
      if (!parts[0]) throw new Error('word/document.xml bulunamadı');
      var theme = null, themeDoc = parseXml(parts[4]);
      if (themeDoc) {
        var major = themeDoc.getElementsByTagNameNS(A, 'majorFont')[0], minor = themeDoc.getElementsByTagNameNS(A, 'minorFont')[0];
        var latin = function (n) { var l = n ? n.getElementsByTagNameNS(A, 'latin')[0] : null; return l ? l.getAttribute('typeface') : null; };
        theme = { major: latin(major) || 'Calibri Light', minor: latin(minor) || 'Calibri' };
      }
      var styles = readStyles(parseXml(parts[1]), theme);
      var ctx = { styles: styles, theme: theme, numbering: readNumbering(parseXml(parts[2])), links: {}, media: {}, losses: {} };

      // Sayfanın varsayılan yazı tipi: belge varsayılanı + varsayılan paragraf stili.
      var base = styles.defaults;
      chain(styles, styles.defaultParagraph).forEach(function (s) { base = merge(base, s.run); });
      ctx.base = { font: base.font || (theme ? theme.minor : 'Calibri'), size: base.size || 10, color: base.color || null };

      var pending = [], relDoc = parseXml(parts[3]);
      if (relDoc) {
        var rels = relDoc.getElementsByTagName('Relationship');
        for (var i = 0; i < rels.length; i++) {
          (function (rel) {
            var id = rel.getAttribute('Id'), target = rel.getAttribute('Target') || '', type = rel.getAttribute('Type') || '';
            if (/\/hyperlink$/.test(type)) { if (/^(https?:|mailto:)/i.test(target)) ctx.links[id] = target; }
            else if (/\/image$/.test(type) && rel.getAttribute('TargetMode') !== 'External') {
              var path = target.charAt(0) === '/' ? target.substring(1) : 'word/' + target.replace(/^\.\//, '');
              var ext = (path.split('.').pop() || '').toLowerCase(), file = zip.file(path);
              if (file && MIME[ext]) pending.push(file.async('base64').then(function (b) { ctx.media[id] = 'data:' + MIME[ext] + ';base64,' + b; }));
              else if (file) ctx.losses.objects = true; // emf/wmf gibi tarayıcının gösteremediği resimler
            }
            else if (/\/(header|footer)$/.test(type)) ctx.losses.headers = true;
          })(rels[i]);
        }
      }
      return Promise.all(pending).then(function () {
        var doc = parseXml(parts[0]);
        var body = kid(doc.documentElement, 'body');
        if (!body) throw new Error('Belge gövdesi bulunamadı');
        var html = new Reader(ctx).blocks(body);
        return { html: html || '<p><br></p>', font: ctx.base.font, size: ctx.base.size, losses: Object.keys(ctx.losses) };
      });
    });
  }

  // ================================================================== YAZMA

  function hex(color) {
    var m = /rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)(?:\s*,\s*([\d.]+))?/.exec(color || '');
    if (!m || (m[4] !== undefined && parseFloat(m[4]) === 0)) return null;
    return [m[1], m[2], m[3]].map(function (n) { var h = parseInt(n, 10).toString(16).toUpperCase(); return h.length < 2 ? '0' + h : h; }).join('');
  }
  function firstFont(family) { return (family || '').split(',')[0].trim().replace(/^['"]|['"]$/g, ''); }
  function px(value) { return parseFloat(value) || 0; }
  function isBold(weight) { return weight === 'bold' || parseInt(weight, 10) >= 600; }

  function Writer(root) {
    this.root = root;
    this.rels = [];       // { id, type, target, external }
    this.media = [];      // { name, base64 }
    this.orderedLists = 0;
    this.pageStyle = getComputedStyle(root);
    this.pageColor = hex(this.pageStyle.color);
  }

  Writer.prototype.rel = function (type, target, external) {
    var id = 'rId' + (this.rels.length + 10);
    this.rels.push({ id: id, type: type, target: target, external: external });
    return id;
  };

  // Bir metin düğümünün biçimi; "base" o blok türünün varsayılan görünümü (aynı olanlar yazılmaz).
  Writer.prototype.runProps = function (node, block, base) {
    var element = node.nodeType === 1 ? node : node.parentElement;
    var cs = getComputedStyle(element), out = '';
    var font = firstFont(cs.fontFamily);
    if (font && font !== base.font) out += '<w:rFonts w:ascii="' + esc(font) + '" w:hAnsi="' + esc(font) + '" w:cs="' + esc(font) + '"/>';
    var bold = isBold(cs.fontWeight);
    if (bold !== base.bold) out += bold ? '<w:b/>' : '<w:b w:val="0"/>';
    if (cs.fontStyle === 'italic' || cs.fontStyle === 'oblique') out += '<w:i/>';

    // Altı/üstü çizgi ve arka plan devralınmaz: bloğa kadar üst öğelere bakılır.
    var underline = false, strike = false, background = null, link = null, vertical = null;
    for (var e = element; e && e !== block.parentElement && e !== this.root; e = e.parentElement) {
      var s = getComputedStyle(e), line = s.textDecorationLine || '';
      if (line.indexOf('underline') >= 0) underline = true;
      if (line.indexOf('line-through') >= 0) strike = true;
      if (!background && e !== block) background = hex(s.backgroundColor);
      if (!link && e.tagName === 'A' && e.getAttribute('href')) link = e.getAttribute('href');
      if (!vertical && (s.verticalAlign === 'super' || s.verticalAlign === 'sub')) vertical = s.verticalAlign;
      if (e === block) break;
    }
    if (strike) out += '<w:strike/>';
    var color = hex(cs.color);
    if (color && color !== base.color) out += '<w:color w:val="' + color + '"/>';
    var size = Math.round(px(cs.fontSize) * 0.75 * 2);
    if (vertical) size = base.size; // üst/alt simgede tarayıcı boyutu küçültür; Word bunu kendisi yapar
    if (size > 0 && size !== base.size) out += '<w:sz w:val="' + size + '"/><w:szCs w:val="' + size + '"/>';
    if (underline) out += '<w:u w:val="single"/>';
    if (background) out += '<w:shd w:val="clear" w:color="auto" w:fill="' + background + '"/>';
    if (vertical) out += '<w:vertAlign w:val="' + (vertical === 'super' ? 'superscript' : 'subscript') + '"/>';
    return { xml: out ? '<w:rPr>' + out + '</w:rPr>' : '', link: link };
  };

  Writer.prototype.textRuns = function (text, rPr) {
    var out = '', parts = text.replace(/\u00A0/g, ' ').split(/(\t|\n)/);
    for (var i = 0; i < parts.length; i++) {
      var part = parts[i];
      if (part === '') continue;
      if (part === '\t') out += '<w:r>' + rPr + '<w:tab/></w:r>';
      else if (part === '\n') out += '<w:r>' + rPr + '<w:br/></w:r>';
      else out += '<w:r>' + rPr + '<w:t xml:space="preserve">' + esc(part) + '</w:t></w:r>';
    }
    return out;
  };

  Writer.prototype.imageRun = function (img) {
    var m = /^data:(image\/[a-z+.-]+);base64,(.+)$/i.exec(img.getAttribute('src') || '');
    if (!m) return '';
    var ext = { 'image/png': 'png', 'image/jpeg': 'jpeg', 'image/gif': 'gif', 'image/bmp': 'bmp' }[m[1].toLowerCase()];
    if (!ext) return '';
    var name = 'image' + (this.media.length + 1) + '.' + ext;
    this.media.push({ name: name, base64: m[2] });
    var id = this.rel('http://schemas.openxmlformats.org/officeDocument/2006/relationships/image', 'media/' + name, false);
    var width = img.offsetWidth || img.naturalWidth || 100, height = img.offsetHeight || img.naturalHeight || 100;
    var maxWidth = 620; // A4 yazı alanı
    if (width > maxWidth) { height = Math.round(height * maxWidth / width); width = maxWidth; }
    var cx = Math.round(width * 9525), cy = Math.round(height * 9525), n = this.media.length;
    return '<w:r><w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0">' +
      '<wp:extent cx="' + cx + '" cy="' + cy + '"/><wp:docPr id="' + n + '" name="Resim ' + n + '"/>' +
      '<a:graphic xmlns:a="' + A + '"><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">' +
      '<pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:nvPicPr><pic:cNvPr id="' + n + '" name="' + name + '"/><pic:cNvPicPr/></pic:nvPicPr>' +
      '<pic:blipFill><a:blip r:embed="' + id + '"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>' +
      '<pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="' + cx + '" cy="' + cy + '"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>' +
      '</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r>';
  };

  var BLOCK_TAGS = { P: 1, DIV: 1, H1: 1, H2: 1, H3: 1, H4: 1, H5: 1, H6: 1, UL: 1, OL: 1, LI: 1, TABLE: 1, BLOCKQUOTE: 1, PRE: 1, HR: 1 };

  // Satır içi içerik → run'lar. İç içe blok görürse durur ve onu "nested" listesine bırakır.
  Writer.prototype.inlineRuns = function (node, block, base, nested) {
    var out = '';
    for (var c = node.firstChild; c; c = c.nextSibling) {
      if (c.nodeType === 3) {
        var text = c.nodeValue;
        if (!text) continue;
        var pre = getComputedStyle(c.parentElement).whiteSpace.indexOf('pre') === 0;
        if (!pre) text = text.replace(/[ \r\n]+/g, ' ');
        if (text === '') continue;
        var props = this.runProps(c, block, base);
        var runs = this.textRuns(text, props.xml);
        if (props.link && /^(https?:|mailto:)/i.test(props.link)) {
          var id = this.rel('http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink', props.link, true);
          runs = '<w:hyperlink r:id="' + id + '" w:history="1">' + runs + '</w:hyperlink>';
        }
        out += runs;
      } else if (c.nodeType === 1) {
        if (c.tagName === 'BR') { if (c.nextSibling) out += '<w:r><w:br/></w:r>'; }
        else if (c.tagName === 'IMG') out += this.imageRun(c);
        else if (BLOCK_TAGS[c.tagName]) nested.push(c);
        else out += this.inlineRuns(c, block, base, nested);
      }
    }
    return out;
  };

  Writer.prototype.baseFor = function (block, heading) {
    // Başlıklarda temel: styles.xml içindeki başlık stili; diğerlerinde belge varsayılanı.
    var page = this.pageStyle;
    var sizes = { 1: 40, 2: 32, 3: 28, 4: 24, 5: 22, 6: 22 };
    return {
      font: firstFont(page.fontFamily),
      size: heading ? sizes[heading] : Math.round(px(page.fontSize) * 0.75 * 2),
      bold: !!heading,
      color: this.pageColor
    };
  };

  Writer.prototype.paragraph = function (el, list) {
    var heading = /^H([1-6])$/.exec(el.tagName), level = heading ? parseInt(heading[1], 10) : 0;
    var cs = getComputedStyle(el), pPr = '';
    if (level) pPr += '<w:pStyle w:val="Heading' + level + '"/>';
    if (list) pPr += '<w:numPr><w:ilvl w:val="' + list.level + '"/><w:numId w:val="' + list.numId + '"/></w:numPr>';
    var align = { center: 'center', right: 'right', end: 'right', justify: 'both' }[cs.textAlign];
    if (align) pPr += '<w:jc w:val="' + align + '"/>';
    if (!list) {
      var left = Math.round(px(cs.marginLeft));
      if (left > 0) pPr += '<w:ind w:left="' + left * 15 + '"/>';
    }
    var nested = [];
    var runs = this.inlineRuns(el, el, this.baseFor(el, level), nested);
    var xml = '<w:p>' + (pPr ? '<w:pPr>' + pPr + '</w:pPr>' : '') + runs + '</w:p>';
    for (var i = 0; i < nested.length; i++) xml += this.block(nested[i], list ? { level: Math.min(list.level + 1, 8), numId: list.numId, parent: true } : null);
    return xml;
  };

  Writer.prototype.list = function (el, parent) {
    var ordered = el.tagName === 'OL', level = parent ? parent.level : 0, numId;
    if (ordered) { this.orderedLists++; numId = 1 + this.orderedLists; } else numId = 1;
    var xml = '';
    for (var li = el.firstElementChild; li; li = li.nextElementSibling) {
      if (li.tagName === 'LI') xml += this.listItem(li, { level: level, numId: numId });
      else if (li.tagName === 'UL' || li.tagName === 'OL') xml += this.list(li, { level: Math.min(level + 1, 8) });
      else xml += this.block(li, null);
    }
    return xml;
  };

  Writer.prototype.listItem = function (li, list) {
    var nested = [], cs = getComputedStyle(li), pPr = '<w:numPr><w:ilvl w:val="' + list.level + '"/><w:numId w:val="' + list.numId + '"/></w:numPr>';
    var align = { center: 'center', right: 'right', end: 'right', justify: 'both' }[cs.textAlign];
    if (align) pPr += '<w:jc w:val="' + align + '"/>';
    var runs = this.inlineRuns(li, li, this.baseFor(li, 0), nested);
    var xml = '<w:p><w:pPr>' + pPr + '</w:pPr>' + runs + '</w:p>';
    for (var i = 0; i < nested.length; i++) {
      var n = nested[i];
      if (n.tagName === 'UL' || n.tagName === 'OL') xml += this.list(n, { level: Math.min(list.level + 1, 8) });
      else xml += this.block(n, null);
    }
    return xml;
  };

  Writer.prototype.table = function (el) {
    var rows = [], maxCols = 0, r, c;
    for (r = 0; r < el.rows.length; r++) {
      var cells = [], width = 0;
      for (c = 0; c < el.rows[r].cells.length; c++) {
        var cell = el.rows[r].cells[c], span = cell.colSpan || 1;
        cells.push({ el: cell, span: span });
        width += span;
      }
      rows.push(cells);
      if (width > maxCols) maxCols = width;
    }
    if (!maxCols) return '';
    var colWidth = Math.floor(9000 / maxCols);
    var border = function (side) { return '<w:' + side + ' w:val="single" w:sz="4" w:space="0" w:color="A0A0A0"/>'; };
    var xml = '<w:tbl><w:tblPr><w:tblW w:w="5000" w:type="pct"/><w:tblBorders>' +
      border('top') + border('left') + border('bottom') + border('right') + border('insideH') + border('insideV') +
      '</w:tblBorders><w:tblLayout w:type="fixed"/></w:tblPr><w:tblGrid>';
    for (c = 0; c < maxCols; c++) xml += '<w:gridCol w:w="' + colWidth + '"/>';
    xml += '</w:tblGrid>';
    for (r = 0; r < rows.length; r++) {
      xml += '<w:tr>';
      for (c = 0; c < rows[r].length; c++) {
        var item = rows[r][c], inner = this.children(item.el);
        var header = item.el.tagName === 'TH';
        xml += '<w:tc><w:tcPr><w:tcW w:w="' + colWidth * item.span + '" w:type="dxa"/>' +
          (item.span > 1 ? '<w:gridSpan w:val="' + item.span + '"/>' : '') +
          (header ? '<w:shd w:val="clear" w:color="auto" w:fill="F0F0F0"/>' : '') + '</w:tcPr>' +
          (inner || '<w:p/>') + '</w:tc>';
      }
      xml += '</w:tr>';
    }
    // Word, tablodan hemen sonra bir paragraf bekler (hücre içinde ya da belge sonunda).
    return xml + '</w:tbl><w:p/>';
  };

  Writer.prototype.block = function (el, list) {
    switch (el.tagName) {
      case 'UL': case 'OL': return this.list(el, list && list.parent ? { level: list.level } : null);
      case 'TABLE': return this.table(el);
      case 'HR': return '<w:p><w:pPr><w:pBdr><w:bottom w:val="single" w:sz="6" w:space="1" w:color="A0A0A0"/></w:pBdr></w:pPr></w:p>';
      case 'LI': return this.listItem(el, list || { level: 0, numId: 1 });
      default: return this.paragraph(el, null);
    }
  };

  // Bir kabın çocukları: bloklar olduğu gibi; araya giren çıplak metin ve satır içi öğeler tek paragrafta toplanır.
  Writer.prototype.children = function (container) {
    var xml = '', runs = '', self = this, base = this.baseFor(container, 0);
    function flush() { if (runs) xml += '<w:p>' + runs + '</w:p>'; runs = ''; }
    for (var c = container.firstChild; c; c = c.nextSibling) {
      if (c.nodeType === 3) {
        var text = c.nodeValue.replace(/[ \r\n]+/g, ' ');
        if (text.trim() !== '' || runs) runs += this.textRuns(text, this.runProps(c, container, base).xml);
      } else if (c.nodeType === 1) {
        if (BLOCK_TAGS[c.tagName]) { flush(); xml += this.block(c, null); }
        else if (c.tagName === 'BR') flush();
        else if (c.tagName === 'IMG') runs += this.imageRun(c);
        else {
          var nested = [];
          runs += this.inlineRuns(c, container, base, nested);
          if (nested.length) { flush(); nested.forEach(function (n) { xml += self.block(n, null); }); }
        }
      }
    }
    flush();
    return xml;
  };

  function numberingXml(orderedLists) {
    var bullets = ['\u2022', 'o', '\u25AA'];
    var xml = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:numbering xmlns:w="' + W + '">';
    function levels(ordered) {
      var out = '';
      for (var i = 0; i < 9; i++) {
        var indent = 720 * (i + 1);
        out += '<w:lvl w:ilvl="' + i + '"><w:start w:val="1"/>' +
          (ordered
            ? '<w:numFmt w:val="' + ['decimal', 'lowerLetter', 'lowerRoman'][i % 3] + '"/><w:lvlText w:val="%' + (i + 1) + '."/>'
            : '<w:numFmt w:val="bullet"/><w:lvlText w:val="' + bullets[i % 3] + '"/>') +
          '<w:lvlJc w:val="left"/><w:pPr><w:ind w:left="' + indent + '" w:hanging="360"/></w:pPr>' +
          (ordered ? '' : '<w:rPr><w:rFonts w:ascii="' + (i % 3 === 1 ? 'Courier New' : 'Arial') + '" w:hAnsi="' + (i % 3 === 1 ? 'Courier New' : 'Arial') + '" w:hint="default"/></w:rPr>') +
          '</w:lvl>';
      }
      return out;
    }
    xml += '<w:abstractNum w:abstractNumId="0"><w:multiLevelType w:val="hybridMultilevel"/>' + levels(false) + '</w:abstractNum>';
    for (var a = 1; a <= orderedLists; a++) {
      xml += '<w:abstractNum w:abstractNumId="' + a + '"><w:multiLevelType w:val="hybridMultilevel"/>' + levels(true) + '</w:abstractNum>';
    }
    xml += '<w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>';
    for (var n = 1; n <= orderedLists; n++) xml += '<w:num w:numId="' + (n + 1) + '"><w:abstractNumId w:val="' + n + '"/></w:num>';
    return xml + '</w:numbering>';
  }

  function stylesXml(font, halfPoints, color) {
    var f = esc(font || 'Calibri');
    var heading = function (n, size) {
      return '<w:style w:type="paragraph" w:styleId="Heading' + n + '"><w:name w:val="heading ' + n + '"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/>' +
        '<w:pPr><w:keepNext/><w:spacing w:before="' + (n === 1 ? 320 : 240) + '" w:after="120"/><w:outlineLvl w:val="' + (n - 1) + '"/></w:pPr>' +
        '<w:rPr><w:b/><w:bCs/><w:sz w:val="' + size + '"/><w:szCs w:val="' + size + '"/></w:rPr></w:style>';
    };
    return '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:styles xmlns:w="' + W + '">' +
      '<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="' + f + '" w:hAnsi="' + f + '" w:eastAsia="' + f + '" w:cs="' + f + '"/>' +
      '<w:sz w:val="' + halfPoints + '"/><w:szCs w:val="' + halfPoints + '"/>' + (color && color !== '000000' ? '<w:color w:val="' + color + '"/>' : '') +
      '<w:lang w:val="tr-TR"/></w:rPr></w:rPrDefault>' +
      '<w:pPrDefault><w:pPr><w:spacing w:after="160" w:line="259" w:lineRule="auto"/></w:pPr></w:pPrDefault></w:docDefaults>' +
      '<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>' +
      heading(1, 40) + heading(2, 32) + heading(3, 28) + heading(4, 24) + heading(5, 22) + heading(6, 22) +
      '<w:style w:type="character" w:default="1" w:styleId="DefaultParagraphFont"><w:name w:val="Default Paragraph Font"/><w:uiPriority w:val="1"/><w:semiHidden/></w:style>' +
      '</w:styles>';
  }

  function writeDocx(root) {
    var writer = new Writer(root);
    var body = writer.children(root);
    if (!body) body = '<w:p/>';
    var page = writer.pageStyle;
    var document = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
      '<w:document xmlns:w="' + W + '" xmlns:r="' + R + '" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing">' +
      '<w:body>' + body +
      '<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="708" w:footer="708" w:gutter="0"/></w:sectPr>' +
      '</w:body></w:document>';

    var rels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
      '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>' +
      '<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>';
    writer.rels.forEach(function (r) {
      rels += '<Relationship Id="' + r.id + '" Type="' + r.type + '" Target="' + esc(r.target) + '"' + (r.external ? ' TargetMode="External"' : '') + '/>';
    });
    rels += '</Relationships>';

    var types = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">' +
      '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>' +
      '<Default Extension="png" ContentType="image/png"/><Default Extension="jpeg" ContentType="image/jpeg"/><Default Extension="gif" ContentType="image/gif"/><Default Extension="bmp" ContentType="image/bmp"/>' +
      '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>' +
      '<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>' +
      '<Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/></Types>';

    var zip = new JSZip();
    zip.file('[Content_Types].xml', types);
    zip.file('_rels/.rels', '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
      '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>');
    zip.file('word/document.xml', document);
    zip.file('word/styles.xml', stylesXml(firstFont(page.fontFamily), Math.round(px(page.fontSize) * 0.75 * 2), writer.pageColor));
    zip.file('word/numbering.xml', numberingXml(writer.orderedLists));
    zip.file('word/_rels/document.xml.rels', rels);
    writer.media.forEach(function (m) { zip.file('word/media/' + m.name, m.base64, { base64: true }); });
    return zip.generateAsync({ type: 'base64', compression: 'DEFLATE' });
  }

  window.ZdpDocx = { read: readDocx, write: writeDocx };
})();
