"""Rewrites Table.cs of a worktree as master ed96417 with CollectDeadKeys collecting every dead key
(master stops after the first). Usage: masterfix.py <worktree>"""
import subprocess, sys
if len(sys.argv) < 2:
    sys.exit(__doc__)
W = sys.argv[1]
src = subprocess.run(['git', '-C', W, 'show', 'ed96417:src/MoonSharp.Interpreter/DataTypes/Table.cs'], capture_output=True, text=True, check=True).stdout
old = '''			for (LinkedListNode<TablePair> node = m_Values.First; node != null; node = node.Next)
			{
				if (node.Value.Value.IsNil())
				{
					Remove(node.Value.Key);
				}
			}'''
new = '''			// PROBE: collect every dead key (master stops after the first because Remove clears node.Next)
			var dead = new List<DynValue>();
			for (LinkedListNode<TablePair> node = m_Values.First; node != null; node = node.Next)
			{
				if (node.Value.Value.IsNil())
					dead.Add(node.Value.Key);
			}
			foreach (DynValue k in dead)
				Remove(k);'''
assert src.count(old) == 1
src = src.replace(old, new)
open(W + '/src/MoonSharp.Interpreter/DataTypes/Table.cs', 'w', encoding='utf-8-sig').write(src.lstrip('﻿'))
print('patched')
