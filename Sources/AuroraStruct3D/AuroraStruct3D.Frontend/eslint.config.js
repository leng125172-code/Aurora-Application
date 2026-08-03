import tsParser from '@typescript-eslint/parser'
import vue from 'eslint-plugin-vue'
import vueParser from 'vue-eslint-parser'

export default [
    {
        ignores: [
            'dist/**',
            'node_modules/**',
            'src/auto-imports.d.ts',
            'src/components/ui/**',
            '../AuroraStruct3D.HttpApi.Host/wwwroot/**',
        ],
    },
    ...vue.configs['flat/essential'],
    {
        files: ['**/*.{ts,vue}'],
        languageOptions: {
            parser: vueParser,
            parserOptions: {
                parser: tsParser,
                ecmaVersion: 'latest',
                sourceType: 'module',
                extraFileExtensions: ['.vue'],
            },
        },
        rules: {
            'vue/multi-word-component-names': 'off',
            'vue/no-v-html': 'off',
            'vue/no-mutating-props': 'off',
        },
    },
]
