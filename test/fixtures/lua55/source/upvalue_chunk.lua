local function outer()
  local x = 40

  local function inc()
    x = x + 1
    return x
  end

  local function get()
    return x
  end

  return inc, get
end

local inc, get = outer()
local a = inc()
local b = get()

return a, b
