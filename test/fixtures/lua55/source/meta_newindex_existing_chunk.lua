local log = {}
local t = setmetatable({ answer = 1 }, {
  __newindex = function(self, key, value)
    log[key] = value
  end
})

t.answer = 42

return t.answer, log.answer
