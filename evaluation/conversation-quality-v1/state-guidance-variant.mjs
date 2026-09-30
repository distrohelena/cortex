// Offline diagnostic transform, never an activation command.
export function createStateGuidanceDiagnostic(source) {
  const result = structuredClone(source);
  const expectedStates = new Set(['participation-inactive-v1', 'participation-attentive-v1', 'participation-engaged-v1']);
  const states = result?.project?.states;
  if (!Array.isArray(states) || states.length !== 3 || new Set(states.map(s => s.id)).size !== 3 ||
      states.some(s => !expectedStates.has(s.id))) throw new Error('Unreviewed state set');
  for (const state of states) {
    for (const id of ['group-generation', 'group-regeneration']) {
      const matches = state.graph.nodes.filter(n => n.id === id);
      if (matches.length !== 1 || matches[0].kind !== 'prompt' || matches[0].useStateGuidance !== true)
        throw new Error(`Unreviewed target: ${state.id}/${id}`);
      matches[0].useStateGuidance = false;
    }
  }
  return result;
}
