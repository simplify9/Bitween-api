# The partner for a run with PARTNER_DELAY_MS: answers every request with the same small JSON
# body after a fixed delay, so a sync call's time is Bitween's own plus a known partner time.
# One thread per request, so the delay never queues calls behind each other.
import os
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

DELAY = int(os.environ.get("PARTNER_DELAY_MS", "100")) / 1000.0
BODY = b'{"accepted":true}'


class Partner(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def answer(self):
        length = int(self.headers.get("Content-Length") or 0)
        if length:
            self.rfile.read(length)
        time.sleep(DELAY)
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(BODY)))
        self.end_headers()
        self.wfile.write(BODY)

    do_GET = do_POST = do_PUT = answer

    def log_message(self, *args):
        pass


ThreadingHTTPServer.daemon_threads = True
ThreadingHTTPServer(("0.0.0.0", 8080), Partner).serve_forever()
