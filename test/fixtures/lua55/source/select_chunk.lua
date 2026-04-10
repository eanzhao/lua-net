local a, b, c = assert("ok", 10, 20)
local n = select("#", a, b, c)
local x, y = select(2, a, b, c)
local z = select(-1, a, b, c)

return n, a, b, c, x, y, z
