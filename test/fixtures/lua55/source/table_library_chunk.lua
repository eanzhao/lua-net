local seq = {10, 20, 30}
local text = table.concat({"a", "b", 3}, "-")
table.insert(seq, 2, 15)
local removed = table.remove(seq, 4)

local moved = {}
table.move(seq, 1, 3, 2, moved)

local sorted = {5, 2, 8, 1}
table.sort(sorted, function(a, b)
  return a > b
end)

local packed = table.pack("x", nil, "z")
local u1, u2, u3 = table.unpack(packed, 1, packed.n)

return
  text,
  seq[1], seq[2], seq[3],
  removed,
  moved[2], moved[3], moved[4],
  sorted[1], sorted[2], sorted[3], sorted[4],
  packed.n,
  u1, u2, u3
