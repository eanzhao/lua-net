local log = {}
local t = setmetatable({}, {
  __newindex = function(self, key, value)
    log[key] = value
  end
})

t.answer = 42

return log.answer
