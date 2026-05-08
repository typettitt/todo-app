import '@vitest/expect';

declare module '@vitest/expect' {
  interface Matchers {
    toHaveNoViolations(): void;
  }
}

export {};
