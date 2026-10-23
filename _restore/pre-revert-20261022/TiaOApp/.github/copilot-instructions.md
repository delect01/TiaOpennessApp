# Copilot Instructions

## Project Guidelines
- For the current installer work, use Debug configuration and defer Release packaging until later.

## Naming Conventions
- For TIA Portal generation, visible device names should use a less restrictive validation than module/internal names, while PROFINET names must retain their existing strict validation rules.
- When comparing TIA Portal MLFB references with Catalog.csv, treat '6ES7 515-2FN03-0AB0' and '6ES7515-2FN03-0AB0' as the same order number with optional spacing after '6ES7'; do not describe them as different device variants.