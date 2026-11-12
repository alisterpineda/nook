import { formatBytes } from './format-bytes';

describe('formatBytes', () => {
  it.each([
    [0, '0 B'],
    [1, '1 B'],
    [1023, '1023 B'],
    [1024, '1.0 KB'],
    [1536, '1.5 KB'],
    [1_468_006, '1.4 MB'],
    [1024 ** 2 - 1, '1.0 MB'],
    [5 * 1024 ** 3, '5.0 GB'],
    [2.5 * 1024 ** 4, '2.5 TB'],
    [3 * 1024 ** 5, '3072.0 TB'],
  ])('formats sizes in binary units with one decimal: %d bytes is %s', (size, expected) => {
    expect(formatBytes(size)).toBe(expected);
  });
});
