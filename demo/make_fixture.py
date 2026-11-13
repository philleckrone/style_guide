# Writes the demo's consultant notes (made-up people and notes, so no real notes are shown):
# demo\fixture\Notes_Ana Consultora.xml and Notes_Luis Revisor.xml, for a copy of PLe11 (a
# consultant notes project) in make_demo.ps1. Run: python make_fixture.py
import io, os

here = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(here, 'fixture')
os.makedirs(out, exist_ok=True)

# (thread, user, ref, date, status, contents xml)
C = [
    ('dm000001', 'Ana Consultora', 'ACT 1:3', '2026-10-01T10:00', 'todo',
     'First draft: <bold>forty days</bold> is fine here.'),
    ('dm000001', 'Luis Revisor', 'ACT 1:3', '2026-10-02T09:30', '',
     'And "many convincing proofs"? We wrote "pruebas".'),
    ('dm000001', 'Ana Consultora', 'ACT 1:3', '2026-10-03T11:15', '',
     '<p>Final note for the team: "<italic>pruebas</italic>" is good, but add "clear".</p>'
     '<p>Compare with Luke 24:39-43.</p>'),
    ('dm000002', 'Ana Consultora', 'ACT 1:5', '2026-10-01T10:20', 'todo',
     'Water and Holy Spirit: make the two halves of the verse match, as in Luke 3:16.'),
    ('dm000003', 'Ana Consultora', 'ACT 1:8', '2026-10-01T10:40', 'todo',
     'Which word do you use for "witnesses"? Use the same word as in Luke 24:48.'),
    ('dm000004', 'Ana Consultora', 'ACT 1:11', '2026-10-01T11:00', 'todo',
     '"This same Jesus": make clear that the one who comes back is the same Jesus they saw go.'),
    ('dm000005', 'Ana Consultora', 'ACT 1:13-14', '2026-10-01T11:20', 'todo',
     'Check the spelling of the names against Luke 6:14-16.'),
    ('dm000006', 'Ana Consultora', 'ACT 1:18', '2026-10-01T11:40', 'todo',
     'Verses 18-19 are an aside by Luke. Consider putting them in parentheses.'),
    ('dm000006', 'Ana Consultora', 'ACT 1:18', '2026-11-03T08:00', '',
     'Sent to PDT15 on 2026-11-03.'),
    ('dm000007', 'Ana Consultora', 'ACT 1:26', '2026-10-01T12:00', 'todo',
     '"Cast lots": readers may not know this custom. A footnote could help.'),
    ('dm000007', 'Luis Revisor', 'ACT 1:26', '2026-10-05T12:00', 'done',
     'Footnote added.'),
    ('dm000008', 'Ana Consultora', 'ACT 2:3', '2026-10-02T10:00', 'todo',
     '"Tongues as of fire": keep "like", so it does not read as real fire.'),
    ('dm000009', 'Ana Consultora', 'ACT 2:4', '2026-10-02T10:20', 'todo',
     '"Other tongues" means other languages: say "languages" here.'),
]

def comment(t, user, ref, date, status, contents):
    return ('  <Comment Thread="%s" User="%s" VerseRef="%s" Language="en" Date="%s:00.0000000-05:00">\n'
            '    <SelectedText />\n    <StartPosition>0</StartPosition>\n    <ContextBefore />\n    <ContextAfter />\n'
            '    <Status>%s</Status>\n    <Type></Type>\n    <ConflictType />\n    <Verse />\n'
            '    <HideInTextWindow>false</HideInTextWindow>\n    <Contents>%s</Contents>\n  </Comment>\n'
            % (t, user, ref, date, status, contents))

for user in sorted(set(c[1] for c in C)):
    body = ''.join(comment(*c) for c in C if c[1] == user)
    p = os.path.join(out, 'Notes_%s.xml' % user)
    io.open(p, 'w', encoding='utf-8', newline='\r\n').write(
        '<?xml version="1.0" encoding="utf-8"?>\n<CommentList>\n' + body + '</CommentList>\n')
    print(p)
