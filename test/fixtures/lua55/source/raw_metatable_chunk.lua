local seen = {}
local mt = {
  __index = { answer = 99 },
  __newindex = seen
}

local t = setmetatable({ 1, 2, answer = 41 }, mt)
local same = rawequal(rawset(t, "created", 42), t) and 1 or 0

return rawget(t, "answer"), rawget(t, "created"), rawget(seen, "created"), rawlen(t), rawlen("lua"), rawequal(getmetatable(t), mt) and 1 or 0, same, rawequal(1, 1.0) and 1 or 0
