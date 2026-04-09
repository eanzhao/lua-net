local function f(...args)
  local y = ...
  return y, args
end

return f(10, 20, 30)
