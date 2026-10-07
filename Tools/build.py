"""Build with an installed Unity Editor. Python 3.9+; standard library only."""
import argparse
import os
from pathlib import Path
import subprocess
import struct
from contextlib import contextmanager

@contextmanager
def linker_compatibility(editor, enabled, backup_folder):
    if not enabled:
        yield
        return
    linker = editor.parent / 'Data/il2cpp/build/deploy/UnityLinker.exe'
    original = linker.read_bytes()
    patched = bytearray(original)
    pe = struct.unpack_from('<I', patched, 0x3c)[0]
    if patched[pe:pe+4] != b'PE\0\0': raise RuntimeError('Unsupported linker executable format.')
    optional = pe + 24
    if struct.unpack_from('<H', patched, optional)[0] != 0x20b: raise RuntimeError('Expected an x64 linker.')
    sections = struct.unpack_from('<H', patched, pe+6)[0]
    optional_size = struct.unpack_from('<H', patched, pe+20)[0]
    rva, size = struct.unpack_from('<II', patched, optional+112+6*8)
    debug = None
    for index in range(sections):
        offset = optional + optional_size + index*40
        virtual_size, address, raw_size, raw = struct.unpack_from('<IIII', patched, offset+8)
        if address <= rva < address+max(virtual_size, raw_size): debug = raw+rva-address; break
    if debug is None: raise RuntimeError('Linker debug directory not found; original file unchanged.')
    for offset in range(debug, debug+size, 28):
        kind, data_size, unused, raw = struct.unpack_from('<IIII', patched, offset+12)
        if kind == 20 and data_size >= 4:
            flags = struct.unpack_from('<I', patched, raw)[0]
            struct.pack_into('<I', patched, raw, flags & ~1)
    if patched == original:
        yield
        return
    backup = backup_folder / 'UnityLinker.exe.original'
    backup.write_bytes(original)
    print('Temporary linker compatibility; original backup:', backup, flush=True)
    try:
        linker.write_bytes(patched)
        yield
    finally:
        linker.write_bytes(original)
        if linker.read_bytes() != original: raise RuntimeError('Could not restore UnityLinker; use the backup.')
        print('Original UnityLinker restored.', flush=True)

parser = argparse.ArgumentParser()
parser.add_argument('--unity', default=os.environ.get('UNITY_EDITOR_PATH'), help='Full path to Unity.exe')
parser.add_argument('--linker-compat', action='store_true', help='Temporarily work around the UnityLinker CET crash on older Windows; original executable is restored.')
args = parser.parse_args()
if not args.unity:
    parser.error('Pass --unity <path to Unity.exe>, or set UNITY_EDITOR_PATH.')
editor = Path(args.unity).expanduser().resolve()
if not editor.is_file(): parser.error('Unity Editor executable not found.')
project = Path(__file__).resolve().parents[1]
log = project / 'Logs/build-windows.log'
log.parent.mkdir(exist_ok=True)
print('Build log:', log, flush=True)
with linker_compatibility(editor, args.linker_compat, log.parent):
    code = subprocess.call([str(editor), '-batchmode', '-quit', '-projectPath', str(project),
        '-buildTarget', 'Win64', '-executeMethod', 'LabBuild.BuildWindows', '-logFile', str(log)], cwd=project)
raise SystemExit(code)
