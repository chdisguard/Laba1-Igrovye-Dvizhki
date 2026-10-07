"""Start the built player invisibly and check its startup log for runtime/asset failures."""
from pathlib import Path
import json
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
project = Path(__file__).resolve().parents[1]
build = json.loads((project / 'Tools/Verification/release-result.json').read_text(encoding='utf-8'))
assert build['passed'] and build['playModePassed']
player = project / 'Builds/Release/SpaceMining.exe'
log = project / 'Tools/Verification/release-player.log'
startup = subprocess.STARTUPINFO()
startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
startup.wShowWindow = 0
process = subprocess.Popen([str(player), '-batchmode', '-screen-fullscreen', '0', '-logFile', str(log)],
    cwd=player.parent, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, startupinfo=startup)
print('Release startup check, PID:', process.pid, flush=True)
deadline = time.monotonic() + 20
while time.monotonic() < deadline and process.poll() is None:
    time.sleep(0.5)
running = process.poll() is None
if running:
    process.terminate()
    process.wait(timeout=10)
text = log.read_text(encoding='utf-8', errors='replace') if log.exists() else ''
failures = [line for line in text.splitlines() if any(marker in line for marker in
    ['NullReferenceException', 'IndexOutOfRangeException', 'MissingReferenceException', 'InvalidOperationException',
     'Shader error', 'Unable to load', 'Failed to load', 'InternalErrorShader', 'ICU Data not available'])]
result = {'passed': running and 'Initialize engine version' in text and 'UnloadTime:' in text and not failures,
          'startup_seconds': 20, 'running_before_test_stop': running, 'errors': failures,
          'detail': 'Player initialized and remained running; stopped by startup check.'}
(project / 'Tools/Verification/release-player-result.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(result, ensure_ascii=False, indent=2))
if not result['passed']: raise SystemExit(1)
