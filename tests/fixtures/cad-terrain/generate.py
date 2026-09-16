# Deterministic ASCII DXF fixtures; authored data only, no production geometry.
from pathlib import Path
root=Path('tests/fixtures/cad-terrain')
def write(name,contours):
    a=[]
    def pair(c,v): a.extend([str(c),str(v)])
    def seq(*values):
        for c,v in zip(values[::2],values[1::2]): pair(c,v)
    seq(0,'SECTION',2,'HEADER',9,'$ACADVER',1,'AC1009',0,'ENDSEC',0,'SECTION',2,'TABLES',0,'TABLE',2,'LAYER',70,6)
    for layer in ['SURVEY','CONTOURS','BOUNDARY','ZERO_Z','NOTES','BLOCK_POINTS']:
        seq(0,'LAYER',2,layer,70,0,62,7,6,'CONTINUOUS')
    seq(0,'ENDTAB',0,'ENDSEC',0,'SECTION',2,'BLOCKS')
    seq(0,'BLOCK',8,'BLOCK_POINTS',2,'TEST_BLOCK',70,0,10,0,20,0,30,0,3,'TEST_BLOCK',1,'')
    seq(0,'POINT',8,'BLOCK_POINTS',10,2,20,3,30,4,0,'ENDBLK',8,'BLOCK_POINTS')
    seq(0,'ENDSEC',0,'SECTION',2,'ENTITIES')
    seq(0,'INSERT',8,'BLOCK_POINTS',2,'TEST_BLOCK',10,30,20,40,30,5,41,1,42,1,43,1,50,90)
    for x,y,z in [(0,0,2),(10,0,2),(10,10,4),(0,10,4),(5,5,3)]:
        seq(0,'POINT',8,'SURVEY',10,x,20,y,30,z)
    def poly(layer,pts,closed):
        seq(0,'POLYLINE',8,layer,66,1,10,0,20,0,30,0,70,9 if closed else 8)
        for x,y,z in pts: seq(0,'VERTEX',8,layer,10,x,20,y,30,z,70,32)
        seq(0,'SEQEND',8,layer)
    if contours:
        poly('CONTOURS',[(0,0,2),(5,0,2),(10,0,2)],False)
        poly('CONTOURS',[(0,10,4),(5,10,4),(10,10,4)],False)
    poly('BOUNDARY',[(0,0,0),(10,0,0),(10,10,0),(0,10,0)],True)
    seq(0,'LINE',8,'ZERO_Z',10,20,20,0,30,0,11,25,21,0,31,0)
    seq(0,'TEXT',8,'NOTES',10,20,20,2,30,0,40,1,1,'EL +3.00')
    seq(0,'ENDSEC',0,'EOF')
    (root/name).write_text('\n'.join(a)+'\n',encoding='ascii')
write('simple-points.dxf',False)
write('contours-3d.dxf',True)
