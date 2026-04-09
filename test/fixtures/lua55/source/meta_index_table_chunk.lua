local fallback = { answer = 42 }
local t = setmetatable({}, { __index = fallback })

return t.answer
