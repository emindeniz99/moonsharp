# Compares a runner output with the Lua 5.2 expectation, case by case.
#   python3 -I compare.py expected/<name>.txt observed/<name>.txt [--show]
# Classes: same; value (both returned, values differ); message (both failed, or
# both returned and only error-message strings differ); fork-errors (Lua 5.2
# returned, MoonSharp failed); fork-accepts (the reverse); crash and hang (the
# MoonSharp process ended; written by run_fork.sh).
import collections
import re
import sys

TOK = re.compile(r'"[^"]*"|\S+')
MSG = re.compile(r'bad argument|attempt to|expected|invalid|cannot|error|overflow|near|out of range|malformed|'
                 r'unfinished|missing|not a number|too |must be|value\)|non-function|\(load\)|^"=?c:|:\(\d|:\d+:')


def classify(r, m):
    if r == m:
        return 'same'
    if m[1] in ('CRASH', 'HANG'):
        return m[1].lower()
    if r[1] in ('ERR', 'SYNTAX') and m[1] in ('ERR', 'SYNTAX'):
        return 'message'
    if r[1] == 'OK' and m[1] == 'OK':
        if r[2] == m[2]:
            rt = TOK.findall(r[3] if len(r) > 3 else '')
            mt = TOK.findall(m[3] if len(m) > 3 else '')
            d = [(a, b) for a, b in zip(rt, mt) if a != b]
            if len(rt) == len(mt) and d and all(
                    a.startswith('"') and b.startswith('"') and (MSG.search(a) or MSG.search(b)) for a, b in d):
                return 'message'
        return 'value'
    return 'fork-errors' if r[1] == 'OK' else 'fork-accepts'


def main():
    ref = [l.rstrip('\n').split('\t') for l in open(sys.argv[1], encoding='latin-1')]
    obs = [l.rstrip('\n').split('\t') for l in open(sys.argv[2], encoding='latin-1')]
    show = '--show' in sys.argv
    counts = collections.Counter()
    for r, m in zip(ref, obs):
        if r[0] != m[0]:
            sys.exit('case order differs: %s / %s' % (r[0], m[0]))
        k = classify(r, m)
        counts[k] += 1
        if show and k != 'same':
            print('%-12s %s\n   5.2: %s\n   ms : %s' % (k, r[0], '\t'.join(r[1:])[:300], '\t'.join(m[1:])[:300]))
    if len(ref) != len(obs):
        sys.exit('case count differs: %d / %d' % (len(ref), len(obs)))
    print(' '.join('%s=%d' % kv for kv in sorted(counts.items())))


main()
