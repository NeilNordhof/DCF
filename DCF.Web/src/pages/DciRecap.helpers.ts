export function computeRanks<T>(items: T[], getValue: (item: T) => number, lowerIsBetter = false): Map<T, number> {
  const sorted = [...items].sort((a, b) => (lowerIsBetter ? getValue(a) - getValue(b) : getValue(b) - getValue(a)));
  const ranks = new Map<T, number>();

  sorted.forEach((item, i) => ranks.set(item, i + 1));

  return ranks;
}
