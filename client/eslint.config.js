import js from '@eslint/js';
import globals from 'globals';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import tseslint from 'typescript-eslint';
import prettier from 'eslint-config-prettier';
import { defineConfig, globalIgnores } from 'eslint/config';

export default defineConfig([
  globalIgnores(['dist', 'coverage', 'playwright-report', 'test-results']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
      prettier,
    ],
    languageOptions: {
      globals: globals.browser,
    },
  },
  // Auth code never touches web storage. The CI grep gate is the primary
  // enforcement; this lint rule shortens the feedback loop locally.
  {
    files: ['src/lib/api*', 'src/lib/api/**', 'src/auth/**', 'src/hooks/useAuth*'],
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          selector: 'MemberExpression[object.name=/^(localStorage|sessionStorage)$/]',
          message: 'Auth must use HttpOnly cookies. Web storage is forbidden in API/auth modules.',
        },
        {
          selector: 'CallExpression[callee.object.name=/^(localStorage|sessionStorage)$/]',
          message: 'Auth must use HttpOnly cookies. Web storage is forbidden in API/auth modules.',
        },
      ],
    },
  },
]);
