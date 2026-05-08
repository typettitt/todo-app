// Node 25 exposes an experimental localStorage getter that warns unless a
// persistence file is configured. MSW only needs to know whether storage exists
// while constructing its cookie store, so shadow it with an explicit test stub.
Object.defineProperty(globalThis, 'localStorage', {
  configurable: true,
  value: undefined,
});
