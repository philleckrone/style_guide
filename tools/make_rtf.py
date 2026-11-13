# -*- coding: utf-8 -*-
<<<<<<< HEAD
# Writes the Style Guide user guide (English; Spanish and French later) from rtf_text.py:
#     python tools/make_rtf.py
# Lines starting with '# ' are headings; '> ' lines are indented; '!img FILE' embeds a PNG
# from the tools folder, scaled to the page width.
import binascii, io, os, re, struct, sys
=======
# Writes the Send Notes to Project user guides (English, Spanish and French) from rtf_text.py:
#     python tools/make_rtf.py
# Lines starting with '# ' are headings; '> ' lines are indented.
import io, os, re, sys
>>>>>>> 057390ea1c9eaaf472f51f6516e34b020467b583

def esc(s):
    out = []
    for ch in s:
        o = ord(ch)
        if ch in u'\\{}':
            out.append(u'\\' + ch)
        elif o < 128:
            out.append(ch)
        elif o < 256:
            out.append(u"\\'%02x" % o)
        else:
            out.append(u'\\u%d?' % (o if o < 32768 else o - 65536))
    return u''.join(out)

<<<<<<< HEAD
HERE = os.path.dirname(os.path.abspath(__file__))
PAGE_WIDTH_TWIPS = 9360   # 6.5 inches

def picture(name):
    data = io.open(os.path.join(HERE, name), 'rb').read()
    width, height = struct.unpack('>II', data[16:24])   # PNG IHDR
    goal_w = PAGE_WIDTH_TWIPS
    goal_h = int(round(goal_w * height / float(width)))
    hexdata = binascii.hexlify(data).decode('ascii')
    lines = [hexdata[i:i + 128] for i in range(0, len(hexdata), 128)]
    return (u'{\\pict\\pngblip\\picw%d\\pich%d\\picwgoal%d\\pichgoal%d\r\n' % (width, height, goal_w, goal_h)
            + u'\r\n'.join(lines) + u'}\\par')

=======
>>>>>>> 057390ea1c9eaaf472f51f6516e34b020467b583
def rtf(title, lines):
    r = [u'{\\rtf1\\ansi\\ansicpg1252\\deff0\\nouicompat{\\fonttbl{\\f0\\fswiss\\fcharset0 Arial;}}',
         u'\\viewkind4\\uc1',
         u'\\pard\\sa200\\sl276\\slmult1\\f0\\fs28\\b ' + esc(title) + u'\\b0\\fs22\\par']
    for ln in lines:
        if ln.startswith(u'# '):
            r.append(u'\\par')
            r.append(u'\\b ' + esc(ln[2:]) + u'\\b0\\par')
        elif ln.startswith(u'> '):
            r.append(u'\\tab ' + esc(ln[2:]) + u'\\par')
<<<<<<< HEAD
        elif ln.startswith(u'!img '):
            r.append(picture(ln[5:].strip()))
=======
>>>>>>> 057390ea1c9eaaf472f51f6516e34b020467b583
        else:
            r.append(esc(ln) + u'\\par')
    r.append(u'}')
    return u'\r\n'.join(r) + u'\r\n'

<<<<<<< HEAD
from rtf_text import EN

ROOT = os.path.join(HERE, '..')
# The version is set once, in Strings.ToolVersion.
VERSION = re.search(r'ToolVersion = "([^"]+)"', io.open(os.path.join(ROOT, 'StyleGuidePlugin', 'Strings.cs'), encoding='ascii').read()).group(1)

def write(name, title, lines):
    io.open(os.path.join(ROOT, 'plugin', name), 'w', encoding='ascii', newline='').write(rtf(title, lines))

write('StyleGuide_UserGuide.rtf', u'STYLE GUIDE %s (PARATEXT PLUGIN)' % VERSION, EN)
=======
from rtf_text import EN, ES, FR

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
# The version is set once, in Strings.ToolVersion.
VERSION = re.search(r'ToolVersion = "([^"]+)"', io.open(os.path.join(ROOT, 'SendNotesPlugin', 'Strings.cs'), encoding='ascii').read()).group(1)
io.open(os.path.join(ROOT, 'plugin', 'SendNotes_UserGuide.rtf'), 'w', encoding='ascii', newline='').write(rtf(u'SEND NOTES TO PROJECT %s (PARATEXT PLUGIN)' % VERSION, EN))
io.open(os.path.join(ROOT, 'plugin', 'SendNotes_UserGuide_sp.rtf'), 'w', encoding='ascii', newline='').write(rtf(u'ENVIAR NOTAS A UN PROYECTO %s (COMPLEMENTO DE PARATEXT)' % VERSION, ES))
io.open(os.path.join(ROOT, 'plugin', 'SendNotes_UserGuide_fr.rtf'), 'w', encoding='ascii', newline='').write(rtf(u"ENVOYER DES NOTES À UN PROJET %s (MODULE D'EXTENSION DE PARATEXT)" % VERSION, FR))
>>>>>>> 057390ea1c9eaaf472f51f6516e34b020467b583
print('ok')
