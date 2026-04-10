local ok1, a, b = xpcall(function(x, y)
  return x, y
end, function(err)
  return "handled:" .. err
end, 40, 2)

local ok2, c = xpcall(ud, function(err)
  return "handled:" .. err
end, 41)

local ok3, err1 = xpcall(function()
  assert(false, "boom")
end, function(err)
  return "handled:" .. err
end)

local ok4, err2 = xpcall(function()
  assert(false, "boom")
end, function(err)
  error("nested")
end)

return ok1 and (a + b) or 0, ok2 and c or 0, ok3 and 1 or 0, err1, ok4 and 1 or 0, err2
