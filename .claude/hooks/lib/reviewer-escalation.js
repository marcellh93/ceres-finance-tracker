// Trip-wire A (Stage 9.5e): the repeat-finding escalation counter.
// Pure logic — orchestrator-side. Given prior counter state + this run's classified
// findings (rule-class strings the 3rd reviewer caught that the first two passed) + the
// diff sha, returns the next state and any graduate-to-analyzer marker.
//
// "Fires" at consecutive == 2 for a class → the marker is the L5 consequence
// (a human-reviewed work-order to promote the rule to a CER0xx analyzer / pre-commit hook).

const FIRE_AT = 2;

function applyEscalation(prior, classifiedFindings, diffSha, knownClasses) {
  const state = { rule_classes: { ...(prior && prior.rule_classes ? prior.rule_classes : {}) } };
  const hit = new Set((classifiedFindings || []).filter((c) => knownClasses.includes(c)));
  let marker = null;

  // Increment classes hit this run (unless this exact diff already counted).
  for (const cls of hit) {
    const cur = state.rule_classes[cls] || { consecutive: 0, last_diff_sha: null };
    if (cur.last_diff_sha === diffSha) { state.rule_classes[cls] = cur; continue; }
    const next = { consecutive: cur.consecutive + 1, last_diff_sha: diffSha };
    state.rule_classes[cls] = next;
    if (next.consecutive >= FIRE_AT && !marker) marker = { rule_class: cls, consecutive: next.consecutive };
  }

  // Reset classes NOT hit this run (a clean read for them).
  for (const cls of Object.keys(state.rule_classes)) {
    if (!hit.has(cls)) state.rule_classes[cls] = { consecutive: 0, last_diff_sha: diffSha };
  }

  return { state, marker };
}

module.exports = { applyEscalation, FIRE_AT };
