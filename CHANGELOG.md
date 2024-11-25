# Change log for DSCParser

# 2.0.0.13

* Removes the logic to do string parsing on the configuration name
  which was causing issues if the word configuration was used anywhere
  in the configs (e.g., comment, values, etc.).