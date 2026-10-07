from pathlib import Path
import json
import subprocess
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
project = Path(__file__).resolve().parents[1]
folder = project / 'Builds/VerificationPlayer'
result = folder / 'mining-result.json'
if result.exists(): result.unlink()
startup = subprocess.STARTUPINFO()
startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
startup.wShowWindow = 0
process = subprocess.Popen([str(folder / 'SpaceMiningVerification.exe'), '-batchmode', '-logFile',
    str(project / 'Tools/Verification/mining-player.log')], cwd=folder, startupinfo=startup,
    stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
print('Standalone mining verification PID:', process.pid, flush=True)
try:
    code = process.wait(timeout=120)
except subprocess.TimeoutExpired:
    process.terminate(); process.wait(timeout=10)
    raise RuntimeError('Standalone mining verification timed out')
assert result.exists(), 'Verification player did not write a result'
report = json.loads(result.read_text(encoding='utf-8'))
print(json.dumps(report, indent=2), flush=True)
assert code == 0 and report['passed'] and report['shapes'] == 7 and report['points'] == 126
(project / 'Tools/Verification/mining-player-result.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
