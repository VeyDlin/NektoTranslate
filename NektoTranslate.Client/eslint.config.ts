import antfu from "@antfu/eslint-config";


export default antfu(
    {
        stylistic: {
            indent: 4,
            quotes: "double",
            semi: true,
        },
        vue: true,
        typescript: true,
    },
    {
        rules: {
            // Two blank lines separate declarations, one sits inside a block. This is the house
            // style across the repository — the C# server is written the same way — so the rule is
            // widened rather than the code reflowed to match a preset.
            "style/no-multiple-empty-lines": ["error", { max: 2, maxBOF: 0, maxEOF: 0 }],
            "style/padded-blocks": "off",
        },
    },
    {
        files: ["**/*.vue"],
        rules: {
            // Template first: the markup is what a reader of a component looks for, and the stack's
            // own convention orders the blocks that way.
            "vue/block-order": ["error", { order: ["template", "script", "style"] }],
            "vue/singleline-html-element-content-newline": "off",

            // Script contents are indented one level inside the block. The two rules cannot both
            // govern a single-file component, so the generic one stands down inside .vue.
            "style/indent": "off",
            "vue/script-indent": ["error", 4, { baseIndent: 1, switchCase: 1 }],
        },
    },
    {
        files: ["**/*.json", "**/*.jsonc"],
        rules: {
            // Alphabetical keys make a tsconfig harder to read, not easier: the groupings that
            // matter there are semantic.
            "jsonc/sort-keys": "off",
        },
    },
);
