import { setupServer } from 'msw/node';

// MSW Node server. Tests register handlers via server.use(...) per case.
export const server = setupServer();
