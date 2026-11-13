# -*- coding: utf-8 -*-
# Send tests for the Send Notes plugin code, through tests\TestHost on scratch copies of a
# consultant notes project and a receiving project (Settings.xml + Notes_*.xml only), plus
# tests\fixtures\Notes_Ana Consultora.xml (a string of three comments at ACT 1:3, a note on
# the words "testigos" at ACT 1:8, a resolved note at ACT 1:9). Run with any Python 3:
#     python tests\run_send_tests.py [SOURCE_PROJECT TARGET_PROJECT]
# Defaults: C:\My Paratext 8 Projects\PLe11 and ...\PDT15. The real projects are never changed.
import io, os, shutil, subprocess, sys, tempfile, glob
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
HOST = os.path.join(HERE, 'TestHost', 'bin', 'Release', 'SendNotesTestHost.exe')
FIXTURE = os.path.join(HERE, 'fixtures', 'Notes_Ana Consultora.xml')
SRC = sys.argv[1] if len(sys.argv) > 2 else r'C:\My Paratext 8 Projects\PLe11'
DST = sys.argv[2] if len(sys.argv) > 2 else r'C:\My Paratext 8 Projects\PDT15'
failures = []


def check(name, ok, detail=''):
    print(('PASS ' if ok else 'FAIL ') + name + ('' if ok else '  ' + detail))
    if not ok:
        failures.append(name)


def copy_project(src, dst):
    os.makedirs(dst)
    for f in ['Settings.xml'] + [os.path.basename(p) for p in glob.glob(os.path.join(src, 'Notes_*.xml'))]:
        shutil.copy(os.path.join(src, f), dst)


def fresh(work):
    if os.path.exists(work):
        shutil.rmtree(work)
    s = os.path.join(work, os.path.basename(SRC))
    d = os.path.join(work, os.path.basename(DST))
    copy_project(SRC, s)
    copy_project(DST, d)
    shutil.copy(FIXTURE, s)
    return s, d


def run(args, env=None):
    e = dict(os.environ)
    for k in ('SN_NOLOCK', 'SN_FAIL', 'SN_LANG', 'SN_USER'):
        e.pop(k, None)
    e.update(env or {})
    out = subprocess.run([HOST] + args, capture_output=True, env=e)
    return out.stdout.decode('utf-8') + out.stderr.decode('utf-8', 'replace')


def comments(folder, user='Test User'):
    p = os.path.join(folder, 'Notes_%s.xml' % user)
    if not os.path.exists(p):
        return []
    return ET.parse(p).getroot().findall('Comment')


def contents_xml(c):
    return ET.tostring(c.find('Contents'), encoding='unicode')


def main():
    work = os.path.join(tempfile.gettempdir(), 'SendNotesTests')
    tname = os.path.basename(DST)

    # 1. list: the string of three comments offers the last one; the resolved note is hidden.
    s, d = fresh(work)
    out = run(['list', s, 'ACT', '1'])
    lines = [l.split('\t') for l in out.splitlines() if l.startswith('fx')]
    by = dict((l[0], l) for l in lines)
    check('list: string of 3 comments, default = 3', 'fx000001' in by and by['fx000001'][3] == '3' and by['fx000001'][4] == '3', out)
    check('list: resolved note hidden', 'fx000003' not in by, out)
    out = run(['list', s, 'ACT', '1', 'resolved'])
    check('list resolved: shown as resolved', any(l.startswith('fx000003\tACT 1:9\tresolved') for l in out.splitlines()), out)

    # 2. send the default (last) comment: arrives at ACT 1:3 with its paragraphs and italic.
    out = run(['send', s, d, 'ACT', '1', 'fx000001'])
    new = comments(d)
    check('send default: one note in target', len(new) == 1, out)
    if new:
        c = new[0]
        x = contents_xml(c)
        check('send default: at ACT 1:3', c.get('VerseRef') == 'ACT 1:3', c.get('VerseRef'))
        check('send default: two paragraphs, italic kept', x.count('<p>') == 2 and '<italic>pruebas</italic>' in x, x)
        check('send default: status todo, language es', c.findtext('Status') == 'todo' and c.get('Language') == 'es', ET.tostring(c, encoding='unicode'))
    marks = [c for c in comments(s) if c.get('Thread') == 'fx000001']
    check('send default: "Sent to" added to source note',
          len(marks) == 1 and (marks[0].findtext('Contents') or '').strip() == 'Sent to %s on 2026-11-03.' % tname,
          str([ET.tostring(m, encoding='unicode') for m in marks]))
    out = run(['list', s, 'ACT', '1'])
    row = [l.split('\t') for l in out.splitlines() if l.startswith('fx000001')][0]
    check('after send: marker recognised, default still comment 3', row[5] == tname + ' 2026-11-03' and row[4] == '3' and row[3] == '4', str(row))

    # 3. choose the first comment: bold kept, single paragraph (no <p>).
    s, d = fresh(work)
    out = run(['send', s, d, 'ACT', '1', 'fx000001:1'])
    new = comments(d)
    x = contents_xml(new[0]) if new else ''
    check('send comment 1: bold kept, no <p>', '<bold>cuarenta d\u00edas</bold>' in x and '<p>' not in x, x + out)

    # 4. a note attached to words keeps them.
    s, d = fresh(work)
    run(['send', s, d, 'ACT', '1', 'fx000002'])
    new = comments(d)
    check('send attached note: words kept', bool(new) and new[0].findtext('SelectedText') == 'testigos' and new[0].get('VerseRef') == 'ACT 1:8',
          ET.tostring(new[0], encoding='unicode') if new else 'none')

    # 5. no lock on the target: nothing is written anywhere.
    s, d = fresh(work)
    before = sorted(os.listdir(d))
    out = run(['send', s, d, 'ACT', '1', 'all'], {'SN_NOLOCK': tname})
    check('no target lock: nothing sent', 'NOTARGETLOCK True' in out and sorted(os.listdir(d)) == before and not comments(s), out)

    # 6. no lock on the source: notes sent, no marker, reported.
    s, d = fresh(work)
    out = run(['send', s, d, 'ACT', '1', 'fx000001', 'fx000002'], {'SN_NOLOCK': os.path.basename(SRC)})
    check('no source lock: notes sent, marker not added', len(comments(d)) == 2 and not comments(s) and 'NOSOURCELOCK True' in out and 'could not be added' in out, out)

    # 7. one note fails: the others are sent and marked, the failure is listed.
    s, d = fresh(work)
    out = run(['send', s, d, 'ACT', '1', 'fx000001', 'fx000002'], {'SN_FAIL': 'ACT 1:8'})
    check('one failure: other sent and marked', len(comments(d)) == 1 and len(comments(s)) == 1 and 'Not sent: ACT 1:8: test failure' in out, out)

    # 8. Spanish marker, recognised afterwards; a whole real chapter.
    s, d = fresh(work)
    out = run(['send', s, d, 'ACT', '1', 'all'], {'SN_LANG': 'es'})
    n = int(out.split('ITEMS ')[1].split()[0])
    check('send all ACT 1 (es): every note sent', len(comments(d)) == n and n > 50, out[:400])
    texts = set((c.findtext('Contents') or '').strip() for c in comments(s))
    check('send all (es): Spanish marker', texts == {'Enviada a %s el 2026-11-03.' % tname}, str(texts))
    out = run(['list', s, 'ACT', '1'])
    rows = [l.split('\t') for l in out.splitlines() if l.strip()]
    check('send all (es): every note now marked sent', all(r[5] == tname + ' 2026-11-03' for r in rows), out[:400])

    # 9. A note on a range of verses keeps its range in the receiving project (1.3).
    s, d = fresh(work)
    fx = os.path.join(s, 'Notes_Ana Consultora.xml')
    with io.open(fx, encoding='utf-8-sig') as f:
        xml = f.read()
    xml = xml.replace(u'</CommentList>', u'  <Comment Thread="fx000009" User="Ana Consultora" VerseRef="ACT 1:13-14" Language="es" '
                      u'Date="2026-10-09T10:00:00.0000000-05:00"><SelectedText /><StartPosition>0</StartPosition>'
                      u'<ContextBefore /><ContextAfter /><Status>todo</Status><Type></Type><ConflictType /><Verse />'
                      u'<HideInTextWindow>false</HideInTextWindow><Contents>Los once apóstoles en el aposento.</Contents>'
                      u'</Comment>\n</CommentList>')
    with io.open(fx, 'w', encoding='utf-8-sig') as f:
        f.write(xml)
    out = run(['send', s, d, 'ACT', '1', 'fx000009'])
    new = comments(d)
    check('range note: sent with its range, ACT 1:13-14', len(new) == 1 and new[0].get('VerseRef') == 'ACT 1:13-14',
          new[0].get('VerseRef') if new else out)
    check('range note: Results show the range on both sides', 'ACT 1:13-14 -> %s ACT 1:13-14' % tname in out, out)

    shutil.rmtree(work, ignore_errors=True)
    print('%d failure(s)' % len(failures))
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
