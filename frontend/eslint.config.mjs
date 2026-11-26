import { defineConfig } from 'eslint/config'
import nextVitals from 'eslint-config-next/core-web-vitals'
import nextTs from 'eslint-config-next/typescript'
import jsxA11y from 'eslint-plugin-jsx-a11y'

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  // eslint-config-next registers the jsx-a11y plugin but turns on only six of
  // its rules. The recommended set adds the keyboard and interaction rules
  // (click-events-have-key-events, interactive-supports-focus and others)
  // that a clickable board needs.
  // The files list matches the one eslint-config-next registers the plugin
  // for; without it, linting a .cjs file fails with "could not find plugin".
  {
    files: ['**/*.{js,jsx,mjs,ts,tsx,mts,cts}'],
    rules: jsxA11y.flatConfigs.recommended.rules,
  },
])

export default eslintConfig
