"""Reads and writes Lua table literals, such as dragon-character/prototypes/sprite-data.lua
and an avatar pack's prototypes/avatar-pack.lua.

Only data is supported, no code: an optional leading `return`, then a value made of
tables, strings, numbers, true, false and nil.

    from lua_table import loads, dumps
    data = loads(open("avatar-pack.lua").read())

Tables with only positional entries become lists, all others dicts (positional
entries get integer keys starting at 1, as in Lua). Entries set to nil are dropped.
"""

import math
import re

__all__ = ["LuaSyntaxError", "loads", "dumps"]


class LuaSyntaxError(ValueError):
    pass


_ESCAPES = {"a": "\a", "b": "\b", "f": "\f", "n": "\n", "r": "\r", "t": "\t", "v": "\v",
            "\\": "\\", '"': '"', "'": "'", "\n": "\n"}
_NUMBER = re.compile(r"0[xX][0-9a-fA-F]+|(?:[0-9]+\.?[0-9]*|\.[0-9]+)(?:[eE][+-]?[0-9]+)?")
_NAME = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")
_LONG_OPEN = re.compile(r"\[(=*)\[")
_DECIMAL_ESCAPE = re.compile(r"[0-9]{1,3}")
_ASSIGN = re.compile(r"[ \t\r\n]*=(?!=)")
_KEYWORDS = {"and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if",
             "in", "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while"}


class _Parser:
    def __init__(self, text):
        self.text = text
        self.pos = 0

    def error(self, message):
        line = self.text.count("\n", 0, self.pos) + 1
        raise LuaSyntaxError(f"line {line}: {message}")

    def skip(self):
        text = self.text
        while self.pos < len(text):
            c = text[self.pos]
            if c in " \t\r\n\f\v":
                self.pos += 1
            elif text.startswith("--", self.pos):
                m = _LONG_OPEN.match(text, self.pos + 2)
                if m:
                    self.pos = self.long_bracket_end(m)
                else:
                    end = text.find("\n", self.pos)
                    self.pos = len(text) if end < 0 else end + 1
            else:
                break

    def long_bracket_end(self, m):
        close = "]" + m.group(1) + "]"
        end = self.text.find(close, m.end())
        if end < 0:
            self.error("unfinished long bracket")
        return end + len(close)

    def peek(self, s):
        self.skip()
        return self.text.startswith(s, self.pos)

    def expect(self, s):
        if not self.peek(s):
            self.error(f"expected {s!r}")
        self.pos += len(s)

    def document(self):
        self.skip()
        m = _NAME.match(self.text, self.pos)
        if m and m.group() == "return":
            self.pos = m.end()
        value = self.value()
        self.skip()
        if self.pos != len(self.text):
            self.error("unexpected text after the value")
        return value

    def value(self):
        self.skip()
        text = self.text
        if self.pos >= len(text):
            self.error("unexpected end of input")
        c = text[self.pos]
        if c == "{":
            return self.table()
        if c in "\"'":
            return self.string()
        if c == "[":
            m = _LONG_OPEN.match(text, self.pos)
            if m:
                return self.long_string(m)
        if c == "-":
            self.pos += 1
            number = self.value()
            if not isinstance(number, (int, float)) or isinstance(number, bool):
                self.error("'-' must be followed by a number")
            return -number
        m = _NUMBER.match(text, self.pos)
        if m and (c.isdigit() or c == "."):
            self.pos = m.end()
            token = m.group()
            if token[:2] in ("0x", "0X"):
                return int(token, 16)
            if any(ch in token for ch in ".eE"):
                return float(token)
            return int(token)
        m = _NAME.match(text, self.pos)
        if m:
            word = m.group()
            if word in ("true", "false", "nil"):
                self.pos = m.end()
                return {"true": True, "false": False, "nil": None}[word]
            self.error(f"unexpected name {word!r} (only data is allowed)")
        self.error(f"unexpected character {c!r}")

    def string(self):
        # Lua strings are bytes: escapes add single bytes, other characters their UTF-8 encoding
        text = self.text
        quote = text[self.pos]
        self.pos += 1
        out = bytearray()
        while True:
            if self.pos >= len(text):
                self.error("unfinished string")
            c = text[self.pos]
            if c == quote:
                self.pos += 1
                try:
                    return out.decode("utf-8")
                except UnicodeDecodeError:
                    self.error("string is not valid UTF-8")
            if c == "\n":
                self.error("unfinished string")
            if c != "\\":
                out += c.encode("utf-8")
                self.pos += 1
                continue
            self.pos += 1
            e = text[self.pos:self.pos + 1]
            if e in _ESCAPES:
                out += _ESCAPES[e].encode("utf-8")
                self.pos += 1
            elif e == "x":
                digits = text[self.pos + 1:self.pos + 3]
                if not re.fullmatch(r"[0-9a-fA-F]{2}", digits):
                    self.error("bad \\x escape")
                out.append(int(digits, 16))
                self.pos += 3
            elif e == "z":
                self.pos += 1
                while self.pos < len(text) and text[self.pos] in " \t\r\n\f\v":
                    self.pos += 1
            elif e and e in "0123456789":
                m = _DECIMAL_ESCAPE.match(text, self.pos)
                if int(m.group()) > 255:
                    self.error("decimal escape too large")
                out.append(int(m.group()))
                self.pos = m.end()
            else:
                # includes \u{...}, which only exists from Lua 5.3 (Factorio uses 5.2)
                self.error(f"invalid escape \\{e}")

    def long_string(self, m):
        end = self.long_bracket_end(m)
        body = self.text[m.end():end - len(m.group(1)) - 2]
        self.pos = end
        # a newline right after the opening bracket is not part of the string
        if body.startswith("\r\n"):
            return body[2:]
        if body.startswith("\n"):
            return body[1:]
        return body

    def table(self):
        self.expect("{")
        keyed = {}
        positional = []
        while not self.peek("}"):
            self.skip()
            text = self.text
            if text.startswith("[", self.pos) and not _LONG_OPEN.match(text, self.pos):
                self.pos += 1
                key = self.value()
                self.expect("]")
                self.expect("=")
                if key is None:
                    self.error("table key is nil")
                if isinstance(key, bool):
                    # Python would merge [true] with [1] and [false] with [0]; Lua keeps them apart
                    self.error("boolean table keys are not supported")
                if isinstance(key, float) and key.is_integer():
                    key = int(key)
                keyed[key] = self.value()
            else:
                m = _NAME.match(text, self.pos)
                eq = m and _ASSIGN.match(text, m.end())
                if eq and m.group() not in _KEYWORDS:
                    self.pos = eq.end()
                    keyed[m.group()] = self.value()
                else:
                    positional.append(self.value())
            if self.peek(",") or self.peek(";"):
                self.pos += 1
            elif not self.peek("}"):
                self.error("expected ',' or '}'")
        self.expect("}")
        keyed = {k: v for k, v in keyed.items() if v is not None}
        if not keyed:
            # trailing nils do not count towards a Lua sequence
            while positional and positional[-1] is None:
                positional.pop()
            if None not in positional:
                return positional
        for i, v in enumerate(positional, 1):
            if v is not None:
                keyed[i] = v
        return keyed


def loads(text):
    """Parses a Lua table literal (optionally preceded by `return`)."""
    return _Parser(text).document()


def _key(key):
    if isinstance(key, str) and _NAME.fullmatch(key) and key not in _KEYWORDS:
        return key
    return "[" + _scalar(key) + "]"


def _scalar(value):
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, int):
        return str(value)
    if isinstance(value, float):
        if not math.isfinite(value):
            raise ValueError("Lua literals can not hold inf or nan")
        return repr(value)
    if isinstance(value, str):
        out = ['"']
        for c in value:
            if c in '"\\':
                out.append("\\" + c)
            elif c == "\n":
                out.append("\\n")
            elif ord(c) < 32 or ord(c) == 127:
                out.append(f"\\{ord(c):03d}")
            else:
                out.append(c)
        out.append('"')
        return "".join(out)
    raise TypeError(f"can not write {type(value).__name__} as a Lua literal")


def _dump(value, indent, level, out):
    if isinstance(value, (list, tuple)):
        if not value:
            out.append("{}")
        elif all(not isinstance(v, (list, tuple, dict)) for v in value) and \
                len(line := "{ " + ", ".join(_scalar(v) for v in value) + " }") <= 80:
            out.append(line)
        else:
            pad = " " * (indent * (level + 1))
            out.append("{\n")
            for v in value:
                out.append(pad)
                _dump(v, indent, level + 1, out)
                out.append(",\n")
            out.append(" " * (indent * level) + "}")
    elif isinstance(value, dict):
        if not value:
            out.append("{}")
            return
        pad = " " * (indent * (level + 1))
        out.append("{\n")
        # numbers first, then strings, each sorted, so output is stable
        for k in sorted(value, key=lambda k: (isinstance(k, str), k)):
            out.append(pad + _key(k) + " = ")
            _dump(value[k], indent, level + 1, out)
            out.append(",\n")
        out.append(" " * (indent * level) + "}")
    else:
        out.append(_scalar(value))


def dumps(value, indent=2):
    """Writes a value as a Lua literal: dict keys sorted, floats in shortest round-trip form."""
    out = []
    _dump(value, indent, 0, out)
    return "".join(out)
