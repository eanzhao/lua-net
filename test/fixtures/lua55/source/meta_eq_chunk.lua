local mt = {
  __eq = function(a, b)
    return a.id == b.id
  end
}

local x = setmetatable({ id = 42 }, mt)
local y = setmetatable({ id = 42 }, mt)

if x == y then
  return 1
end

return 0
