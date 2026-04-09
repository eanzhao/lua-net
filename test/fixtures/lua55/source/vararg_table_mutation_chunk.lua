local function f(...args)
  args[2] = 99
  return args[2], args.n
end

return f(10, 20, 30)
