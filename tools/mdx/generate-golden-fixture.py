"""Build the MDX 2.0 golden fixture from the checked-in UTF-8 TSV."""

from html import escape
from pathlib import Path
from struct import pack
from zlib import adler32, compress


def compress_block(data):
    return pack("<I", 2) + pack(">I", adler32(data)) + compress(data, level=9)


def build_mdx(entries):
    header = ('<Dictionary GeneratedByEngineVersion="2.0" RequiredEngineVersion="2.0" '
              'Encoding="UTF-8" Format="Html" Encrypted="0" Title="Golden dictionary"/>\0').encode("utf-16le")
    key_index = bytearray()
    record_index = bytearray()
    key_blocks = bytearray()
    record_blocks = bytearray()
    record_offset = 0
    batch_size = 25

    for start in range(0, len(entries), batch_size):
        batch = entries[start:start + batch_size]
        keys = bytearray()
        records = bytearray()
        for word, definition in batch:
            keys += pack(">Q", record_offset + len(records)) + word.encode("utf-8") + b"\0"
            records += f"<p><b>{escape(definition)}</b></p>\0".encode("utf-8")

        first, last = batch[0][0].encode("utf-8"), batch[-1][0].encode("utf-8")
        compressed_keys = compress_block(keys)
        compressed_records = compress_block(records)
        key_index += pack(">QH", len(batch), len(first)) + first + b"\0"
        key_index += pack(">H", len(last)) + last + b"\0"
        key_index += pack(">QQ", len(compressed_keys), len(keys))
        record_index += pack(">QQ", len(compressed_records), len(records))
        key_blocks += compressed_keys
        record_blocks += compressed_records
        record_offset += len(records)

    block_count = len(record_index) // 16
    compressed_key_index = compress_block(key_index)
    key_header = pack(">5Q", block_count, len(entries), len(key_index), len(compressed_key_index), len(key_blocks))
    record_header = pack(">4Q", block_count, len(entries), len(record_index), len(record_blocks))
    return b"".join((pack(">I", len(header)), header, pack("<I", adler32(header)),
                     key_header, pack(">I", adler32(key_header)), compressed_key_index, key_blocks,
                     record_header, record_index, record_blocks))


if __name__ == "__main__":
    fixtures = Path(__file__).resolve().parents[2] / "tests" / "Flint3.Data.Tests" / "Fixtures"
    entries = sorted(line.split("\t", 1) for line in (fixtures / "mdx-golden.tsv").read_text(encoding="utf-8").splitlines())
    assert len(entries) == len({word for word, _ in entries}) == 100
    (fixtures / "mdx-golden.mdx").write_bytes(build_mdx(entries))
