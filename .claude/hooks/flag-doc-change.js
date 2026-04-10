const file = process.argv[2] || "";

const docWorthyPatterns = [
  /Models\//i,
  /Controllers\//i,
  /Data\//i,
  /migrations/i,
];

const isDocWorthy = docWorthyPatterns.some((p) => p.test(file));

if (isDocWorthy) {
  console.log(
    `Note: ${file} was modified — consider running /sync-docs ` +
      `if this involved a schema or behavior change.`,
  );
}
