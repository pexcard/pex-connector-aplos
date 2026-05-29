# PEX Design System -- Vendored Source

Source repo: /Start (projects/ngx-pex-core/src/lib/styles/)
Last synced: 2026-05-22

## Clarity versions at sync time
- @clr/ui: 17.9.0 (Start) / ^17.0.0 (connector)
- @cds/core: 6.15.1 (Start) / ^6.16.1 (connector)

## Vendored files
| File | Source | Lines |
|------|--------|-------|
| _pex-tokens.scss | pex-tokens.scss | 228 |
| _pex-themes.scss | pex-themes.scss | 170 |
| _pex-clarity-styles.scss | pex-clarity-styles.scss | 231 |
| _pex-mixins.scss | pex-mixins.scss | 98 |
| _pex-styles.scss | pex-styles.scss | 47 |
| logos/ (10 SVGs) | logos/ | -- |

## Partner theme
| File | Source |
|------|--------|
| partners/_aplos.scss | src/styles/partners/aplos.scss |

## Assets
| File | Source |
|------|--------|
| assets/logos/aplos.svg | src/assets/logos/aplos.svg |

## Update procedure
1. Diff each SCSS file against its source in Start
2. Diff logos/ directory for new or changed SVGs
3. Apply changes, preserving the `// Vendored from:` comment block
4. Run: npm run build -- --configuration production
5. Run: npm test
6. Visual check: header, buttons, links, wizard
7. Update "Last synced" date above
