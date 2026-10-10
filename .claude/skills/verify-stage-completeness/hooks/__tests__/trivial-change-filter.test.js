const { test } = require("node:test");
const assert = require("node:assert");
const { diffIsTrivial } = require("../evidence-bundle-check.js");

// A unified-diff hunk helper: joins lines so the classifier sees real +/- markers.
const diff = (...lines) => lines.join("\n");

test("using-only removal is trivial (the 947f0563 case)", () => {
  const d = diff(
    "diff --git a/ProjectCeres/Services/UserAnonymiser.cs b/...",
    "--- a/ProjectCeres/Services/UserAnonymiser.cs",
    "+++ b/ProjectCeres/Services/UserAnonymiser.cs",
    "@@ -3,4 +3,3 @@",
    "-using ProjectCeres.Common;",
    "-using ProjectCeres.Common.Authentication;",
  );
  assert.strictEqual(diffIsTrivial(d), true);
});

test("migration losing `using System;` + BOM reflow is trivial", () => {
  const d = diff(
    "@@ -1,2 +1,1 @@",
    "-﻿using System;",
    "-using Microsoft.EntityFrameworkCore.Migrations;",
    "+using Microsoft.EntityFrameworkCore.Migrations;",
  );
  // Removed two usings, re-added one (BOM stripped) — all lines are bare usings/blank.
  assert.strictEqual(diffIsTrivial(d), true);
});

test("a real statement change is NOT trivial", () => {
  const d = diff(
    "@@ -40,3 +40,3 @@",
    "-        var warned = 0;",
    "+        var warned = 1;",
  );
  assert.strictEqual(diffIsTrivial(d), false);
});

test("mixed: one using removed + one logic line is NOT trivial", () => {
  const d = diff(
    "@@ -3,5 +3,4 @@",
    "-using ProjectCeres.Common;",
    "-        await DoRealThing();",
  );
  assert.strictEqual(diffIsTrivial(d), false);
});

test("empty / unreadable diff is NOT trivial (conservative — gate normally)", () => {
  assert.strictEqual(diffIsTrivial(""), false);
  assert.strictEqual(diffIsTrivial(null), false);
});

test("whitespace-only reflow is trivial", () => {
  const d = diff("@@ -10,2 +10,2 @@", "-    foo();", "+        foo();", "-", "+   ");
  // `foo();` indentation change is NOT a bare using and NOT blank → not trivial.
  assert.strictEqual(diffIsTrivial(d), false);
});

test("blank-line-only change is trivial", () => {
  const d = diff("@@ -1,1 +1,2 @@", "+", "+   ");
  assert.strictEqual(diffIsTrivial(d), true);
});
