"""从 ECDICT 的 stardict.db 生成 Wordle 词表（Flint3.Core/Wordle/wordle_words.json）。

用法：python tools/wordle/gen_wordle_words.py <stardict.db> <输出 json>
- answers：5 个 a-z 字母且带 bnc/frq 词频的常用词，按字母序排列，保证同一天答案稳定
- allowed：全部 5 个 a-z 字母的词，用于校验玩家输入
"""
import json
import sqlite3
import sys

db_path = sys.argv[1] if len(sys.argv) > 1 else "Flint3/Data/stardict.db"
out_path = sys.argv[2] if len(sys.argv) > 2 else "Flint3.Core/Wordle/wordle_words.json"

con = sqlite3.connect(db_path)
allowed = [r[0] for r in con.execute(
    "select word from stardict "
    "where length(word)=5 and word glob '[a-z][a-z][a-z][a-z][a-z]' "
    "group by word order by word")]
answers = [r[0] for r in con.execute(
    "select word from stardict "
    "where length(word)=5 and word glob '[a-z][a-z][a-z][a-z][a-z]' "
    "and (bnc > 0 or frq > 0) "
    "group by word order by word")]

with open(out_path, "w", encoding="utf-8") as f:
    json.dump({"answers": answers, "allowed": allowed}, f, ensure_ascii=False, separators=(",", ":"))

print(f"answers={len(answers)} allowed={len(allowed)} out={out_path}")
