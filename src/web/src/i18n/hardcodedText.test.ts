import ts from 'typescript'
import en from './locales/en.json'

// Every screen's source, read as text by Vite (tests, fakes, generated client and shadcn primitives excluded).
const sources = import.meta.glob<string>(
  [
    '/src/**/*.{ts,tsx}',
    '!/src/**/*.test.{ts,tsx}',
    '!/src/test/**',
    '!/src/api/generated/**',
    '!/src/components/ui/**',
  ],
  { query: '?raw', import: 'default', eager: true },
)

/** Attributes people read or hear; their text must come from i18next too. */
const textAttributes = new Set([
  'aria-label',
  'aria-description',
  'placeholder',
  'title',
  'alt',
  'label',
])
const words = /[A-Za-z\u0980-\u09FF]{2,}/ // Latin or Bangla letters
const pluralSuffixes = ['zero', 'one', 'two', 'few', 'many', 'other']

type Messages = { [key: string]: string | Messages }

function hasKey(messages: Messages, key: string): boolean {
  const lookup = (k: string): boolean => {
    let node: string | Messages | undefined = messages
    for (const part of k.split('.')) node = typeof node === 'object' ? node[part] : undefined
    return typeof node === 'string'
  }
  return lookup(key) || pluralSuffixes.some((s) => lookup(`${key}_${s}`))
}

interface Finding {
  hardcoded: string[]
  unknownKeys: string[]
}

function scan(path: string, text: string, out: Finding): void {
  const file = ts.createSourceFile(path, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX)
  const where = (node: ts.Node) =>
    `${path}:${String(file.getLineAndCharacterOfPosition(node.getStart()).line + 1)}`

  const visit = (node: ts.Node): void => {
    if (ts.isJsxText(node) && words.test(node.text)) {
      out.hardcoded.push(`${where(node)} ${node.text.trim()}`)
    }
    if (
      ts.isJsxAttribute(node) &&
      textAttributes.has(node.name.getText(file)) &&
      node.initializer &&
      ts.isStringLiteral(node.initializer) &&
      words.test(node.initializer.text)
    ) {
      out.hardcoded.push(`${where(node)} ${node.getText(file)}`)
    }
    // t('literal.key') must exist; template keys (`status.${s}`) are checked by their own tests.
    if (ts.isCallExpression(node)) {
      const callee = node.expression.getText(file)
      const first = node.arguments[0]
      if (
        (callee === 't' || callee.endsWith('.t')) &&
        first &&
        ts.isStringLiteralLike(first) &&
        !hasKey(en, first.text)
      ) {
        out.unknownKeys.push(`${where(node)} ${first.text}`)
      }
    }
    ts.forEachChild(node, visit)
  }
  visit(file)
}

describe('user-facing text', () => {
  const found: Finding = { hardcoded: [], unknownKeys: [] }
  for (const [path, text] of Object.entries(sources)) scan(path, text, found)

  it('scans the app source', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(20)
  })

  it('has no text typed straight into JSX: use t()', () => {
    expect(found.hardcoded).toEqual([])
  })

  it('only uses translation keys that exist', () => {
    expect(found.unknownKeys).toEqual([])
  })
})
