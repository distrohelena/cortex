import { expect, test, vi } from 'vitest';
const remote = vi.hoisted(() => ({ close: vi.fn(), promises: {} }));
vi.mock('@authzed/authzed-node', () => ({ v1: { NewClient: () => remote, ClientSecurity: { INSECURE_LOCALHOST_ALLOWED: 1 } } }));
import { SpiceDbClient } from './spicedb.js';
test('owned client closes its external channel on supervisor shutdown', () => {
  const client = new SpiceDbClient({ endpoint: 'localhost:1', token: 'unit-only', insecure: true });
  expect(typeof client.close).toBe('function');
  client.close();
  expect(remote.close).toHaveBeenCalledTimes(1);
});
