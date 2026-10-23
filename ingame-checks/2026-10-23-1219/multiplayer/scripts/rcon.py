"""Sends commands to a Factorio server over RCON and prints the replies.

    py -3 rcon.py <command> [<command> ...]        (each argument is one console line, e.g. "/sc rcon.print(1)")
    py -3 rcon.py --file <commands.txt>             (one console line per line; blank lines and # comments skipped)

Server: --rcon-port 27015 --rcon-password test (override with RCON_PORT / RCON_PASSWORD).
"""
import os
import socket
import struct
import sys
import time

HOST = "127.0.0.1"
PORT = int(os.environ.get("RCON_PORT", "27015"))
PASSWORD = os.environ.get("RCON_PASSWORD", "test")


def packet(request_id, kind, body):
    payload = struct.pack("<ii", request_id, kind) + body.encode("utf-8") + b"\x00\x00"
    return struct.pack("<i", len(payload)) + payload


def receive(sock):
    def exactly(n):
        data = b""
        while len(data) < n:
            chunk = sock.recv(n - len(data))
            if not chunk:
                raise ConnectionError("RCON connection closed")
            data += chunk
        return data

    size = struct.unpack("<i", exactly(4))[0]
    data = exactly(size)
    request_id, kind = struct.unpack("<ii", data[:8])
    return request_id, kind, data[8:-2].decode("utf-8", "replace")


def main(args):
    if args[:1] == ["--file"]:
        with open(args[1], encoding="utf-8") as f:
            commands = [line.rstrip("\n") for line in f
                        if line.strip() and (not line.lstrip().startswith("#") or line.startswith("#sleep "))]
    else:
        commands = args
    sock = socket.create_connection((HOST, PORT), timeout=30)
    sock.sendall(packet(1, 3, PASSWORD))
    while True:
        request_id, kind, _ = receive(sock)
        if kind == 2:
            break
    if request_id == -1:
        sys.exit("RCON: wrong password")
    for n, command in enumerate(commands, start=2):
        if command.startswith("#sleep "):
            time.sleep(float(command.split()[1]))
            continue
        sock.sendall(packet(n, 2, command))
        _, _, body = receive(sock)
        print(f"> {command[:160]}")
        if body.strip():
            print(body.rstrip())
    sock.close()


if __name__ == "__main__":
    main(sys.argv[1:])
