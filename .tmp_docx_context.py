from docx import Document
import sys

doc = Document(sys.argv[1])
ranges = [(70, 92), (137, 151), (219, 236), (311, 333), (389, 410), (467, 491), (548, 570), (624, 646), (704, 718), (904, 954)]
for start, end in ranges:
    print(f"\n--- {start}:{end} ---")
    for i in range(start, end):
        p = doc.paragraphs[i]
        text = " ".join(p.text.split())
        if text or p._p.xpath('.//w:drawing'):
            drawing = " [IMAGE]" if p._p.xpath('.//w:drawing') else ""
            print(f"{i:04d} | {p.style.name!r} | {text}{drawing}")
