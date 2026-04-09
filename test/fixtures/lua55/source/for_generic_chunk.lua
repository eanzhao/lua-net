local function iter(state, control)
  local next = control + 1
  if next <= state then
    return next, next * 10
  end

  return nil
end

local sum = 0

for i, v in iter, 3, 0 do
  sum = sum + i + v
end

return sum
