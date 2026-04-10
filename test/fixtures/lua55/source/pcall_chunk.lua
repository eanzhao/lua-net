local ok1, a, b = pcall(function()
  return 40, 2
end)

local ok2, c = pcall(ud, 41)

local ok3, err = pcall(function()
  error("boom")
end)

local ok4, msg = pcall(assert, false, "assert-fail")

return ok1 and (a + b) or 0, ok2 and c or 0, ok3 and 1 or 0, err, ok4 and 1 or 0, msg
