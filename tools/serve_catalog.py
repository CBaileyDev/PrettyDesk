"""Loopback-only HTTPS server for signed catalog acceptance testing. No global trust changes."""
import argparse
import functools
import http.server
import ssl
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--root", type=Path, required=True)
parser.add_argument("--cert", type=Path, required=True)
parser.add_argument("--key", type=Path, required=True)
parser.add_argument("--port", type=int, default=30443)
args = parser.parse_args()
context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
context.load_cert_chain(args.cert, args.key)
server = http.server.ThreadingHTTPServer(("127.0.0.1", args.port), functools.partial(http.server.SimpleHTTPRequestHandler, directory=str(args.root)))
server.socket = context.wrap_socket(server.socket, server_side=True)
print(f"Serving signed catalog fixture on https://localhost:{args.port}/", flush=True)
server.serve_forever()
