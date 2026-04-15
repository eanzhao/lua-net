local s = "Aπ文"
local s1, e1 = utf8.offset(s, 1, 1)
local s2, e2 = utf8.offset(s, 2, 1)
local c1, c2, c3 = utf8.codepoint(s, 1, -1)
local rebuilt = utf8.char(c1, c2, c3)

local positions = {}
local values = {}
for pos, code in utf8.codes(s) do
  positions[#positions + 1] = pos
  values[#values + 1] = code
end

return
  utf8.len(s),
  s1, e1,
  s2, e2,
  c1, c2, c3,
  rebuilt,
  positions[1], values[1],
  positions[2], values[2],
  positions[3], values[3],
  utf8.charpattern ~= nil
