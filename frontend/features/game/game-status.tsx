// The heading of the panel beside the board, with a line under it, such as
// "Your move" over "White to move · move 6".
export function GameStatus({
  title,
  detail,
}: {
  title: string
  detail: string
}) {
  return (
    <div className="flex flex-col gap-0.5">
      <h1 className="font-display text-display-s font-semibold">{title}</h1>
      <p className="text-body-s text-muted-foreground">{detail}</p>
    </div>
  )
}
