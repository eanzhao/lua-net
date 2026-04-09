local target = {}
local t = setmetatable({}, { __newindex = target })

t.answer = 42

return target.answer, t.answer
