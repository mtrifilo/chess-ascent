// @vitest-environment node
import { renderToString } from 'react-dom/server'
import { describe, expect, it } from 'vitest'

import { ChessBoard } from './chess-board'

// Server rendering runs without a DOM. Chessground touches `window` and
// `document` only once the board is created in the browser, so the board's
// module loads and renders here as an empty, named frame.
describe('ChessBoard on the server', () => {
  it('renders without a DOM', () => {
    const html = renderToString(
      <ChessBoard fen="rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1" />
    )

    expect(html).toContain('aria-label="Chess board"')
    expect(html).not.toContain('cg-board')
  })
})
