// Offline presentation repair. Callers must derive evidence from the scoped native
// journal; this function neither resolves identity from names nor executes messages.
export function repairHistoryNames(history, evidence) {
  const output = structuredClone(history);
  const fields = ['ChannelId', 'AccountId', 'NativeConversationId', 'ProviderMessageId'];
  const key = row => {
    if (fields.some(field => typeof row[field] !== 'string' || !row[field])) throw new Error('invalid_history_scope');
    return JSON.stringify(fields.map(field => row[field]));
  };
  const validName = name => typeof name === 'string' && name.trim().length > 0
    && name.length <= 256 && !/[\u0000-\u001f\u007f-\u009f]/u.test(name);
  const names = new Map();
  for (const proof of evidence) {
    const id = key(proof);
    if (!proof.ActorId || !validName(proof.DisplayName) || names.has(id)) throw new Error('invalid_or_ambiguous_name_evidence');
    names.set(id, proof);
  }
  const prior = new Map();
  for (const row of output) {
    const id = key(row);
    if (prior.has(id)) throw new Error('duplicate_history_message');
    const sender = row.Message?.remetente;
    const proof = names.get(id);
    if (row.Direction === 0 && sender?.stableId === proof?.ActorId && proof
        && !sender.nome?.trim()) sender.nome = proof.DisplayName;
    const quote = row.Message?.reply;
    if (quote?.id && !quote.remetente?.nome?.trim()) {
      const target = prior.get(key({ ...row, ProviderMessageId: quote.id }));
      const speaker = target?.Message?.remetente;
      if (speaker?.stableId && validName(speaker.nome)
          && typeof quote.texto === 'string' && quote.texto === target.Message.texto
          && (!quote.remetente?.stableId || quote.remetente.stableId === speaker.stableId))
        quote.remetente = structuredClone(speaker);
    }
    prior.set(id, row);
  }
  return output;
}
