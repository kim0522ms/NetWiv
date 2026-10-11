// Deterministic codec fixtures, generated from explicit pixels without external assets.
import { mkdirSync, writeFileSync } from 'node:fs';
import { deflateSync } from 'node:zlib';
import { fileURLToPath } from 'node:url';

const directory = fileURLToPath(new URL('../frontend/images/', import.meta.url));
mkdirSync(directory, { recursive: true });
const crc32 = bytes => {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0);
  }
  return (crc ^ 0xffffffff) >>> 0;
};
const chunk = (type, bytes) => {
  const length = Buffer.alloc(4), crc = Buffer.alloc(4), content = Buffer.concat([Buffer.from(type), bytes]);
  length.writeUInt32BE(bytes.length); crc.writeUInt32BE(crc32(content));
  return Buffer.concat([length, content, crc]);
};
const png = (width, depth, color, pixels, extra = []) => {
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width); header.writeUInt32BE(1, 4); header[8] = depth; header[9] = color;
  return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]), chunk('IHDR', header), ...extra,
    chunk('IDAT', deflateSync(Buffer.from([0, ...pixels]))), chunk('IEND', Buffer.alloc(0))]);
};
const rgba = png(2, 8, 6, [255,0,0,255,0,255,0,128]);
writeFileSync(directory + 'rgba.png', rgba);
writeFileSync(directory + 'gray16.png', png(3, 16, 0, [0,0,128,128,255,255]));
writeFileSync(directory + 'palette.png', png(2, 8, 3, [0,1], [
  chunk('PLTE', Buffer.from([255,0,0,0,255,0])), chunk('tRNS', Buffer.from([255,128]))]));
const entries = [png(1,8,6,[0,0,255,255]), rgba];
const header = Buffer.alloc(6 + 16 * entries.length);
header.writeUInt16LE(1, 2); header.writeUInt16LE(entries.length, 4);
let offset = header.length;
entries.forEach((bytes, index) => {
  const start = 6 + index * 16;
  header[start] = index + 1; header[start + 1] = 1;
  header.writeUInt16LE(1, start + 4); header.writeUInt16LE(32, start + 6);
  header.writeUInt32LE(bytes.length, start + 8); header.writeUInt32LE(offset, start + 12);
  offset += bytes.length;
});
writeFileSync(directory + 'multi.ico', Buffer.concat([header, ...entries]));
