import type { Game } from '@/features/game/game-state'

import answer from './special-moves-game.json'

// A finished game as the API answers GET /games/{id} for it, saved as it
// came in special-moves-game.json: a game in which the player moves both
// sides, with an en passant capture (3. exf6), castling on each side
// (6... O-O-O, 7. O-O), a promotion (8. gxh8=Q) and an under-promotion
// (11... bxa1=N), which White wins with 14. Qxf8#. Every position the replay
// shows is the one the API sent for that move, so none is typed by hand.
//
// The file is the one list of the game's moves. The API tests play them and
// fail if the API's answer differs from the file in anything but the game's
// id and time (GameEndpointsTests.EachMoveGivesThePositionAfterIt). To make
// the file again, create a game with POST /games and an opponent of kind
// "none", post each move's uci to POST /games/{id}/moves in order, and save
// the answer to GET /games/{id}. Imported only by tests.
export const SPECIAL_MOVES_GAME = answer as Game
