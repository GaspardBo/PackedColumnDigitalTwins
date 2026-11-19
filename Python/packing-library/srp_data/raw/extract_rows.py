import os, sys, pymupdf, re
R=os.path.dirname(os.path.abspath(__file__))+'/'  # put the two thesis PDFs here (links in ../README.md)
def rows(f,p0,p1,out):
    d=pymupdf.open(R+f); o=open(out,'w',encoding='utf-8')
    for i in range(p0-1,p1):
        W=d[i].get_text('words'); W.sort(key=lambda w:(round(w[1]/3),w[0]))
        lines=[];cur=[];y=None
        for w in sorted(W,key=lambda w:(w[1],w[0])):
            if y is None or abs(w[1]-y)>3:
                if cur: lines.append(cur)
                cur=[w];y=w[1]
            else: cur.append(w)
        if cur: lines.append(cur)
        for l in lines: o.write(f'p{i+1}\t'+' '.join(x[4] for x in sorted(l,key=lambda w:w[0]))+'\n')
    o.close()
d=pymupdf.open(R+'wang_2015_dissertation.pdf')
# find page range of appendix C / D in Wang, B.9 / C in Tsai by searching text
def find(f,s):
    d=pymupdf.open(R+f); return [i+1 for i,p in enumerate(d) if s in p.get_text()]
print('wang C', find('wang_2015_dissertation.pdf','Table C.1')[:3], 'D', find('wang_2015_dissertation.pdf','Appendix D')[:3])
print('tsai B9', find('tsai_2010_dissertation.pdf','Table B.9')[:3], 'C', find('tsai_2010_dissertation.pdf','Appendix C: Interfacial')[:3])
rows('wang_2015_dissertation.pdf',144,165,'wang_rows.txt')
rows('tsai_2010_dissertation.pdf',356,381,'tsai_rows.txt')
