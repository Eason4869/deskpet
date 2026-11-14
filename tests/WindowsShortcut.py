"""Unicode-only shell-link fixtures. Never use WSH's ACP-dependent automation API."""
import ctypes as C
from ctypes import wintypes as W
import uuid

class Guid(C.Structure):
    _fields_=[('data',C.c_ubyte*16)]
    def __init__(self,value):
        super().__init__();self.data[:]=uuid.UUID(value).bytes_le

def _call(obj,slot,types,*args):
    table=C.cast(obj,C.POINTER(C.POINTER(C.c_void_p))).contents
    result=C.WINFUNCTYPE(C.c_long,C.c_void_p,*types)(table[slot])(obj,*args)
    if result<0:raise OSError('Shell-link HRESULT 0x%08x'%(result&0xffffffff))
    return result

class Shortcut:
    def __init__(self,path,load=False):
        self.path=str(path);self.link=C.c_void_p();self.file=C.c_void_p()
        ole=C.OleDLL('ole32')
        ole.CoInitialize.argtypes=[C.c_void_p]
        ole.CoCreateInstance.argtypes=[C.POINTER(Guid),C.c_void_p,W.DWORD,C.POINTER(Guid),C.POINTER(C.c_void_p)]
        ole.CoUninitialize.argtypes=[];ole.CoUninitialize.restype=None
        ole.CoInitialize(None)
        self.ole=ole
        cls=Guid('00021401-0000-0000-C000-000000000046');iid=Guid('000214F9-0000-0000-C000-000000000046')
        try:
            ole.CoCreateInstance(C.byref(cls),None,1,C.byref(iid),C.byref(self.link))
            iid=Guid('0000010B-0000-0000-C000-000000000046')
            _call(self.link,0,[C.POINTER(Guid),C.POINTER(C.c_void_p)],C.byref(iid),C.byref(self.file))
            if load:_call(self.file,5,[W.LPCWSTR,W.DWORD],self.path,0)
        except:
            self.close();raise
    def close(self):
        for name in ('file','link'):
            obj=getattr(self,name)
            if obj:
                _call(obj,2,[]);setattr(self,name,C.c_void_p())
        if self.ole:self.ole.CoUninitialize();self.ole=None
    def __enter__(self):return self
    def __exit__(self,*unused):self.close()
    def set(self,target,working_directory='',arguments='',description='',icon='',icon_index=0,hotkey=0,show_command=1):
        for slot,value in ((20,str(target)),(9,str(working_directory)),(11,arguments),(7,description)):
            _call(self.link,slot,[W.LPCWSTR],value)
        _call(self.link,17,[W.LPCWSTR,C.c_int],str(icon),icon_index)
        _call(self.link,13,[W.WORD],hotkey)
        _call(self.link,15,[C.c_int],show_command)
        _call(self.file,6,[W.LPCWSTR,W.BOOL],self.path,True)
    def read(self):
        values={}
        for key,slot in (('target',3),('description',6),('working_directory',8),('arguments',10),('icon',16)):
            value=C.create_unicode_buffer(32768)
            types=[W.LPWSTR,C.c_int];args=[value,len(value)]
            if key=='target':types += [C.c_void_p,W.DWORD];args += [None,4]
            if key=='icon':
                index=C.c_int();types += [C.POINTER(C.c_int)];args += [C.byref(index)]
            _call(self.link,slot,types,*args);values[key]=value.value
            if key=='icon':values['icon_index']=index.value
        for key,slot,typ in (('hotkey',12,W.WORD),('show_command',14,C.c_int)):
            value=typ();_call(self.link,slot,[C.POINTER(typ)],C.byref(value));values[key]=value.value
        return values

def create_shortcut(path,target,**options):
    with Shortcut(path) as link:
        link.set(target,**options)
    with Shortcut(path,load=True) as link:
        result=link.read()
    assert result['target']==str(target),'Unicode shortcut target must round-trip exactly'
    return result
