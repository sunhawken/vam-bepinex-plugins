import re

def del_method(t, sig_regex):
    """Remove a method (and any attribute/blank lines directly before it) whose header matches sig_regex."""
    m = re.search(sig_regex, t)
    if not m:
        raise SystemExit('del_method: not found ' + sig_regex)
    start = t.rfind('\n', 0, m.start()) + 1
    i = t.index('{', m.end())
    depth = 0
    j = i
    while True:
        c = t[j]
        if c == '{':
            depth += 1
        elif c == '}':
            depth -= 1
            if depth == 0:
                break
        j += 1
    end = j + 1
    while end < len(t) and t[end] in '\r\n':
        end += 1
    return t[:start] + t[end:]

def del_type(t, sig_regex):
    return del_method(t, sig_regex)

def rep(t, a, b, count=1):
    if a not in t:
        raise SystemExit('rep: not found: ' + a[:80])
    return t.replace(a, b, count)
