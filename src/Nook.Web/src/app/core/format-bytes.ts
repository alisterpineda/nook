const units = ['KB', 'MB', 'GB', 'TB'] as const;

/**
 * A size in bytes as people read it: whole bytes below 1 KB, and otherwise in the smallest binary unit (1 KB is 1024
 * bytes), up to TB, that keeps the number below 1024 once rounded, with one decimal, such as "1.4 MB".
 */
export function formatBytes(size: number): string {
  if (size < 1024) {
    return `${size} B`;
  }
  let value = size / 1024;
  let unit = 0;
  while (unit < units.length - 1 && Number(value.toFixed(1)) >= 1024) {
    value /= 1024;
    unit++;
  }
  return `${value.toFixed(1)} ${units[unit]}`;
}
