"""Merge the per-drawing PDFs in the given order (bookmarks = drawing names). Usage: merge_pdf.py <dir> <order.txt> <out.pdf> <title>"""
import os, sys
import pymupdf
d, order, out, title = sys.argv[1], open(sys.argv[2]).read().split(), sys.argv[3], sys.argv[4]
doc = pymupdf.open(); toc = []
for i, name in enumerate(order):
    src = pymupdf.open(os.path.join(d, name + '.pdf'))
    doc.insert_pdf(src, from_page=0, to_page=0)
    toc.append([1, name.replace('___', ' & ').replace('_', ' '), i + 1])
doc.set_toc(toc)
doc.set_metadata({'title': title, 'author': 'Program Diagram plugin'})
doc.save(out, garbage=3, deflate=True)
print(doc.page_count, os.path.getsize(out) // 1024, 'KB')
