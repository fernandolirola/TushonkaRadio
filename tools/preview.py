from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import argparse, json, subprocess
root = Path(__file__).resolve().parent.parent
class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(root / "Player"), **kwargs)
    def do_POST(self):
        if self.path != "/api/command":
            self.send_error(404)
            return
        origin = self.headers.get("Origin", "")
        if origin and origin != "http://" + self.headers.get("Host", ""):
            self.send_error(403)
            return
        length = int(self.headers.get("Content-Length", "0"))
        if length < 1 or length > 8192:
            self.send_error(400)
            return
        try:
            command = json.loads(self.rfile.read(length))
            if command.get("type") in ("ready", "refresh", "youtube-list", "youtube-audio"):
                dll = root / "tools/Checks/bin/Release/net10.0/Checks.dll"
                proc = subprocess.run(["dotnet", str(dll), "--bridge"], input=json.dumps(command), capture_output=True, text=True, encoding="utf-8", timeout=135)
                result = json.loads(proc.stdout)
            else:
                result = {"type": "ack"}
        except Exception as exc:
            result = {"type": "youtube-error", "request": command.get("request", ""), "error": str(exc)}
        data = json.dumps(result).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)
    def do_GET(self):
        if self.path.split("?")[0] == "/dev.html":
            data = (root / "tools/Checks/lifecycle.html").read_bytes()
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)
        else:
            super().do_GET()
if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=8766)
    args = parser.parse_args()
    print(f"Hideout Radio preview: http://127.0.0.1:{args.port}", flush=True)
    ThreadingHTTPServer(("127.0.0.1", args.port), Handler).serve_forever()
