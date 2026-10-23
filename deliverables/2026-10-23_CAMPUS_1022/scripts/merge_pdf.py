"""Merge the per-drawing PDFs in the given order (bookmarks = drawing names). Usage: merge_pdf.py <dir> <order.txt> <out.pdf> <title> [--sheet WxH]
--sheet 1224x792: a smaller page (legend, pie) is placed at true size in the middle of an 11x17 sheet; larger ones keep their own size."""
import os, sys
import pymupdf
d, order, out, title = sys.argv[1], open(sys.argv[2]).read().split(), sys.argv[3], sys.argv[4]
sheet = tuple(float(v) for v in sys.argv[sys.argv.index("--sheet") + 1].split("x")) if "--sheet" in sys.argv else None
doc = pymupdf.open(); toc = []
for i, name in enumerate(order):
    src = pymupdf.open(os.path.join(d, name + '.pdf'))
    r = src[0].rect
    if sheet and r.width <= sheet[0] + 0.5 and r.height <= sheet[1] + 0.5 and (r.width, r.height) != sheet:
        pg = doc.new_page(width=sheet[0], height=sheet[1])
        x, y = (sheet[0] - r.width) / 2, (sheet[1] - r.height) / 2
        pg.show_pdf_page(pymupdf.Rect(x, y, x + r.width, y + r.height), src, 0)
    else:
        doc.insert_pdf(src, from_page=0, to_page=0)
    toc.append([1, name.replace('___', ' & ').replace('_', ' '), i + 1])
doc.set_toc(toc)
doc.set_metadata({'title': title, 'author': 'Program Diagram plugin'})
doc.save(out, garbage=3, deflate=True)
print(doc.page_count, os.path.getsize(out) // 1024, 'KB')
